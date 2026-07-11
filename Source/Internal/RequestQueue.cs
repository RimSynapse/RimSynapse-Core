using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Dynamic request queue with per-mod budget enforcement, tick-aware throttling,
    /// dynamic priority scaling, and TTL dropping.
    /// Serializes LLM requests (one at a time) and scales back under load.
    /// </summary>
    public static class RequestQueue
    {
        /// <summary>A queued LLM request with mod attribution and scoring info.</summary>
        public class QueuedRequest
        {
            public SynapseModHandle Mod;
            public List<ChatMessage> Messages;
            public ChatOptions Options;
            public Action<ChatResult> Callback;
            public int Priority;
            public DateTime EnqueuedAt;
            public double CurrentScore;
        }

        private static readonly List<QueuedRequest> _queue = new List<QueuedRequest>();
        private static readonly object _queueLock = new object();

        private static readonly Thread _workerThread;
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private static volatile bool _shutdown;

        // Throttle state
        private static float _throttleLevel = 1.0f;
        private static readonly List<long> _recentDurations = new List<long>();
        private static DateTime _windowStart = DateTime.UtcNow;
        private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(60);
        private static DateTime _lastModelWarning = DateTime.MinValue;

        // Public stats
        public static int QueueDepth
        {
            get
            {
                lock (_queueLock)
                {
                    return _queue.Count;
                }
            }
        }
        
        public static float ThrottleLevel => _throttleLevel;
        public static bool IsProcessing { get; private set; }
        
        public static QueuedRequest ActiveRequest { get; private set; }
        public static System.Diagnostics.Stopwatch ActiveRequestStopwatch { get; private set; } = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Average response time in milliseconds across recent requests.
        /// </summary>
        public static float AverageResponseMs
        {
            get
            {
                lock (_recentDurations)
                {
                    if (_recentDurations.Count == 0) return 0f;
                    long sum = 0;
                    foreach (var d in _recentDurations) sum += d;
                    return sum / (float)_recentDurations.Count;
                }
            }
        }

        static RequestQueue()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "RimSynapse-RequestQueue",
            };
            _workerThread.Start();
        }

        /// <summary>
        /// Expose a snapshot of the queue for the debug monitor.
        /// </summary>
        public static List<QueuedRequest> GetQueueSnapshot()
        {
            lock (_queueLock)
            {
                return _queue.ToList();
            }
        }

        public static int SessionId { get; private set; }

        /// <summary>
        /// Clears all pending requests from the queue.
        /// </summary>
        public static void Clear()
        {
            lock (_queueLock)
            {
                SessionId++;
                foreach (var req in _queue)
                {
                    if (req.Mod != null)
                    {
                        req.Mod.QueuedCount = Math.Max(0, req.Mod.QueuedCount - 1);
                    }
                }
                _queue.Clear();
            }
            SynapseLogger.Message("RequestQueue cleared.");
        }

        /// <summary>
        /// Enqueue a chat completion request. Budget-checked and priority-ordered.
        /// </summary>
        internal static void Enqueue(SynapseModHandle mod, List<ChatMessage> messages,
            ChatOptions options, Action<ChatResult> callback)
        {
            var request = new QueuedRequest
            {
                Mod = mod,
                Messages = messages,
                Options = options,
                Callback = callback,
                Priority = options?.priority ?? 0,
                EnqueuedAt = DateTime.UtcNow,
                CurrentScore = 0
            };

            if (mod != null)
            {
                mod.QueuedCount++;
            }

            lock (_queueLock)
            {
                _queue.Add(request);
            }
            
            _signal.Set(); // Wake the worker

            SynapseLogger.Debug("queue",
                $"Request enqueued for {mod?.DisplayName ?? "unknown"}. Queue depth: {QueueDepth}.",
                mod?.ModId);
        }

        private static int _activeRequests = 0;

        /// <summary>
        /// Background worker loop. Processes requests in parallel based on dynamic scoring.
        /// </summary>
        private static void WorkerLoop()
        {
            while (!_shutdown)
            {
                var settings = RimSynapseMod.Instance?.Settings;
                int maxConcurrent = settings?.maxConcurrentRequests ?? 2;

                if (_activeRequests >= maxConcurrent)
                {
                    _signal.WaitOne(TimeSpan.FromMilliseconds(100));
                    continue;
                }

                // Wait for a request or periodic check if queue is empty
                bool hasItems = false;
                lock (_queueLock) { hasItems = _queue.Count > 0; }
                if (!hasItems)
                {
                    _signal.WaitOne(TimeSpan.FromSeconds(1));
                    if (_shutdown) break;
                }

                // Reset window counters if window expired
                if (DateTime.UtcNow - _windowStart > WindowDuration)
                {
                    _windowStart = DateTime.UtcNow;
                    ModRegistry.ResetWindowCounters();
                }

                QueuedRequest requestToProcess = null;
                var now = DateTime.UtcNow;

                lock (_queueLock)
                {
                    if (_queue.Count == 0)
                    {
                        // Queue is empty, attempt opportunistic background work
                        if (_activeRequests == 0)
                        {
                            OpportunisticTaskManager.TryRunOpportunisticTask();
                        }
                        continue;
                    }

                    int maxPerWindow = settings?.maxRequestsPerMinute ?? 30;

                    // Evaluate queue: Drop stale requests and calculate dynamic scores
                    for (int i = _queue.Count - 1; i >= 0; i--)
                    {
                        var req = _queue[i];
                        double ageMs = (now - req.EnqueuedAt).TotalMilliseconds;

                        // TTL Check
                        if (req.Options?.maxWaitMs.HasValue == true && ageMs > req.Options.maxWaitMs.Value)
                        {
                            SynapseLogger.Warning($"Request for {req.Mod?.DisplayName} dropped (exceeded maxWaitMs of {req.Options.maxWaitMs.Value}).");
                            if (req.Mod != null) req.Mod.QueuedCount = Math.Max(0, req.Mod.QueuedCount - 1);
                            
                            var cb = req.Callback;
                            SynapseGameComponent.Enqueue(() => cb?.Invoke(ChatResult.Failure("Request timed out in queue.")));
                            
                            _queue.RemoveAt(i);
                            continue;
                        }

                        // Calculate Token Penalty
                        int totalChars = 0;
                        if (req.Messages != null)
                        {
                            foreach (var msg in req.Messages)
                            {
                                totalChars += msg.content?.Length ?? 0;
                            }
                        }
                        double estimatedTokens = totalChars / 4.0;
                        double tokenPenalty = estimatedTokens * 50.0;

                        // Calculate Dynamic Score: (Priority * 100,000) + Capped Age - Token Penalty
                        double cappedAge = Math.Min(ageMs, 100000.0);
                        req.CurrentScore = (req.Priority * 100000.0) + cappedAge - tokenPenalty;

                        // Budget Penalty: If over budget, apply massive penalty
                        if (req.Mod != null && !ModRegistry.IsWithinBudget(req.Mod, maxPerWindow))
                        {
                            req.CurrentScore -= 10000000.0;
                        }
                    }

                    if (_queue.Count == 0)
                        continue;

                    // Sort descending by score and pick the top
                    _queue.Sort((a, b) => b.CurrentScore.CompareTo(a.CurrentScore));
                    
                    requestToProcess = _queue[0];
                    _queue.RemoveAt(0);
                }

                if (requestToProcess == null) continue;

                int currentSession = SessionId;

                // ── Prevent firing if game is not active ──
                if (Verse.Current.ProgramState != Verse.ProgramState.Playing)
                {
                    SynapseLogger.Message($"Discarding request for {requestToProcess.Mod?.DisplayName} because the game is not playing.");
                    if (requestToProcess.Mod != null)
                    {
                        requestToProcess.Mod.QueuedCount = Math.Max(0, requestToProcess.Mod.QueuedCount - 1);
                    }
                    continue;
                }

                if (requestToProcess.Mod != null)
                {
                    requestToProcess.Mod.QueuedCount = Math.Max(0, requestToProcess.Mod.QueuedCount - 1);
                }

                // Execute in parallel
                IsProcessing = true;
                ActiveRequest = requestToProcess;
                ActiveRequestStopwatch.Restart();

                Interlocked.Increment(ref _activeRequests);
                var capturedReq = requestToProcess;
                var capturedSessionId = currentSession;

                System.Threading.Tasks.Task.Run(() => 
                {
                    try
                    {
                        ProcessRequest(capturedReq, capturedSessionId);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeRequests);
                        _signal.Set();
                        
                        if (_activeRequests == 0)
                        {
                            IsProcessing = false;
                        }
                    }
                });
            }
        }

        private static void ProcessRequest(QueuedRequest requestToProcess, int currentSession)
        {
            try
            {
                SynapseLogger.Info("queue",
                    $"Processing request for {requestToProcess.Mod?.DisplayName ?? "unknown"}. Score: {requestToProcess.CurrentScore:F0}",
                    requestToProcess.Mod?.ModId);

                string model = ModelManager.ResolveModel(requestToProcess.Options?.model);
                if (!string.IsNullOrEmpty(model) && requestToProcess.Options != null)
                {
                    requestToProcess.Options.model = model;
                }

                // Context injection
                if (SynapseCoreContext.IsEnabled() &&
                    !string.IsNullOrEmpty(requestToProcess.Options?.eventType))
                {
                    try
                    {
                        var contextText = SynapseCoreContext.GetContextText(
                            requestToProcess.Options.eventType,
                            requestToProcess.Options.sourcePawn,
                            requestToProcess.Options.targetPawn,
                            requestToProcess.Options.contextTiers ?? requestToProcess.Mod?.DefaultTiers,
                            requestToProcess.Options.weightOverrides);

                        if (!string.IsNullOrEmpty(contextText))
                        {
                            string systemPrompt = requestToProcess.Mod?.SystemPrompt
                                ?? SynapseCoreContext.ResolvePrompt(
                                    requestToProcess.Options.eventType,
                                    requestToProcess.Mod?.ModId);

                            string fullSystemMessage = string.IsNullOrEmpty(systemPrompt)
                                ? contextText
                                : $"{systemPrompt}\n---\n{contextText}";

                            var sysMsg = requestToProcess.Messages.Find(m => m.role == "system");
                            if (sysMsg != null)
                            {
                                sysMsg.content = fullSystemMessage;
                            }
                            else
                            {
                                requestToProcess.Messages.Insert(0, new ChatMessage
                                {
                                    role = "system",
                                    content = fullSystemMessage,
                                });
                            }
                        }
                    }
                    catch (Exception ctxEx)
                    {
                        SynapseLogger.Warning($"Context assembly failed: {ctxEx.Message}");
                    }
                }

                // ── Debug: Log full prompt ──
                if (RimSynapseMod.Instance?.Settings?.traceDebugMode == true)
                {
                    var promptLog = new System.Text.StringBuilder();
                    promptLog.AppendLine($"── PROMPT → {requestToProcess.Mod?.DisplayName ?? "unknown"} ──");
                    foreach (var msg in requestToProcess.Messages)
                    {
                        promptLog.AppendLine($"[{msg.role}]: {msg.content}");
                    }
                    SynapseLogger.Message(promptLog.ToString(), requestToProcess.Mod?.ModId);
                }

                // Synchronous HTTP call inside thread pool
                var result = HttpEngine.PostChatCompletionSync(
                    requestToProcess.Messages, requestToProcess.Options);

                if (SessionId != currentSession)
                {
                    SynapseLogger.Message($"Session changed during request. Discarding response for {requestToProcess.Mod?.DisplayName}.");
                    return;
                }

                result.wasThrottled = false; // Throttling removed
                ModRegistry.RecordRequest(requestToProcess.Mod);

                // ── Info: Concise completion and timing ──
                SynapseLogger.Message($"Completed LLM request for {requestToProcess.Mod?.DisplayName ?? "unknown"} in {result.durationMs}ms. (Success: {result.success})", requestToProcess.Mod?.ModId);

                // ── Debug: Log full response ──
                if (RimSynapseMod.Instance?.Settings?.traceDebugMode == true)
                {
                    var respLog = new System.Text.StringBuilder();
                    respLog.AppendLine($"── RESPONSE ← {requestToProcess.Mod?.DisplayName ?? "unknown"} ({result.durationMs}ms, success={result.success}) ──");
                    if (result.success)
                    {
                        respLog.AppendLine(result.content);
                    }
                    else
                    {
                        respLog.AppendLine($"ERROR: {result.error}");
                    }
                    SynapseLogger.Message(respLog.ToString(), requestToProcess.Mod?.ModId);
                }

                // ── Handle Model Swap Errors ──
                if (!result.success && !string.IsNullOrEmpty(result.error))
                {
                    string errLower = result.error.ToLowerInvariant();
                    if (errLower.Contains("model not found") || errLower.Contains("404") || (errLower.Contains("400") && errLower.Contains("model")))
                    {
                        ModelManager.RefreshCache();
                        
                        var settings = RimSynapseMod.Instance?.Settings;
                        if (settings != null && !settings.autoMapModel)
                        {
                            if ((DateTime.UtcNow - _lastModelWarning).TotalSeconds > 10)
                            {
                                _lastModelWarning = DateTime.UtcNow;
                                SynapseGameComponent.Enqueue(() => {
                                    Verse.Messages.Message(
                                        "RimSynapse LLM Error: Model not found. Did you swap models in LM Studio? Please open RimSynapse Core Mod Settings and reselect your model (or enable Auto-map).",
                                        RimWorld.MessageTypeDefOf.RejectInput, false);
                                });
                            }
                        }
                    }
                }

                lock (_recentDurations)
                {
                    _recentDurations.Add(result.durationMs);
                    if (_recentDurations.Count > 20)
                        _recentDurations.RemoveAt(0);
                }

                var cb = requestToProcess.Callback;
                SynapseGameComponent.Enqueue(() => cb?.Invoke(result));
            }
            catch (Exception ex)
            {
                SynapseLogger.Error($"Queue worker error: {ex.Message}");
                if (SessionId == currentSession)
                {
                    var cb = requestToProcess.Callback;
                    SynapseGameComponent.Enqueue(() =>
                        cb?.Invoke(ChatResult.Failure($"Queue error: {ex.Message}")));
                }
            }
        }

        private static void UpdateThrottle()
        {
            _throttleLevel = 1.0f;
        }

        internal static void Shutdown()
        {
            _shutdown = true;
            _signal.Set();
        }
    }
}
