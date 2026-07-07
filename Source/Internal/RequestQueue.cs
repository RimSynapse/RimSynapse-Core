using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace RimSynapse.Internal
{
    /// <summary>
    /// Dynamic request queue with per-mod budget enforcement and tick-aware throttling.
    /// Serializes LLM requests (one at a time) and scales back under load.
    /// </summary>
    internal static class RequestQueue
    {
        /// <summary>A queued LLM request with mod attribution.</summary>
        private class QueuedRequest
        {
            public SynapseModHandle Mod;
            public List<ChatMessage> Messages;
            public ChatOptions Options;
            public Action<ChatResult> Callback;
            public int Priority;
            public DateTime EnqueuedAt;
        }

        private static readonly ConcurrentQueue<QueuedRequest> _queue =
            new ConcurrentQueue<QueuedRequest>();

        private static readonly Thread _workerThread;
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private static volatile bool _shutdown;

        // Throttle state
        private static float _throttleLevel = 1.0f;
        private static readonly List<long> _recentDurations = new List<long>();
        private static DateTime _windowStart = DateTime.UtcNow;
        private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(60);

        // Public stats
        internal static int QueueDepth => _queue.Count;
        internal static float ThrottleLevel => _throttleLevel;
        internal static bool IsProcessing { get; private set; }

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
            };

            if (mod != null)
            {
                mod.QueuedCount++;
            }

            _queue.Enqueue(request);
            _signal.Set(); // Wake the worker

            SynapseLog.Debug("queue",
                $"Request enqueued for {mod?.DisplayName ?? "unknown"}. Queue depth: {_queue.Count}.",
                mod?.ModId);
        }

        /// <summary>
        /// Background worker loop. Processes one request at a time.
        /// </summary>
        private static void WorkerLoop()
        {
            while (!_shutdown)
            {
                // Wait for a request or periodic check
                _signal.WaitOne(TimeSpan.FromSeconds(1));

                if (_shutdown) break;

                // Reset window counters if window expired
                if (DateTime.UtcNow - _windowStart > WindowDuration)
                {
                    _windowStart = DateTime.UtcNow;
                    ModRegistry.ResetWindowCounters();
                    UpdateThrottle();
                }

                // Process next request
                if (!_queue.TryDequeue(out var request))
                    continue;

                if (request.Mod != null)
                {
                    request.Mod.QueuedCount = Math.Max(0, request.Mod.QueuedCount - 1);
                }

                // Budget check — if over budget, deprioritize (re-enqueue behind others)
                var settings = RimSynapseMod.Instance?.Settings;
                int maxPerWindow = settings?.maxRequestsPerMinute ?? 30;

                if (request.Mod != null && !ModRegistry.IsWithinBudget(request.Mod, maxPerWindow))
                {
                    // Check if there are under-budget requests waiting
                    if (_queue.Count > 0)
                    {
                        SynapseLog.Debug("queue",
                            $"Mod \"{request.Mod.DisplayName}\" over budget. Re-enqueueing.",
                            request.Mod.ModId);

                        // Re-enqueue at the back
                        request.Mod.QueuedCount++;
                        _queue.Enqueue(request);

                        // Apply throttle delay to avoid busy-spinning
                        Thread.Sleep(100);
                        continue;
                    }
                    // If only over-budget requests remain, process anyway (don't stall)
                }

                // Throttle delay
                if (_throttleLevel < 1.0f)
                {
                    int delayMs = (int)((1.0f - _throttleLevel) * 2000);
                    if (delayMs > 0)
                    {
                        SynapseLog.Debug("queue",
                            $"Throttle delay: {delayMs}ms (level: {_throttleLevel:F2}).");
                        Thread.Sleep(delayMs);
                    }
                }

                // Execute
                IsProcessing = true;
                var wasThrottled = _throttleLevel < 0.9f;

                try
                {
                    SynapseLog.Info("queue",
                        $"Processing request for {request.Mod?.DisplayName ?? "unknown"}. " +
                        $"Remaining: {_queue.Count}.",
                        request.Mod?.ModId);

                    // Resolve model
                    string model = ModelManager.ResolveModel(request.Options?.model);
                    if (!string.IsNullOrEmpty(model) && request.Options != null)
                    {
                        request.Options.model = model;
                    }

                    // Synchronous wait for the HTTP call to complete
                    var doneEvent = new ManualResetEventSlim(false);

                    HttpEngine.PostChatCompletion(request.Messages, request.Options, result =>
                    {
                        result.wasThrottled = wasThrottled;
                        ModRegistry.RecordRequest(request.Mod);

                        // Track duration for throttle calculations
                        lock (_recentDurations)
                        {
                            _recentDurations.Add(result.durationMs);
                            if (_recentDurations.Count > 20)
                                _recentDurations.RemoveAt(0);
                        }

                        request.Callback?.Invoke(result);
                        doneEvent.Set();
                    });

                    // Wait for completion (with timeout)
                    int timeoutMs = (settings?.timeoutSeconds ?? 120) * 1000 + 5000;
                    if (!doneEvent.Wait(timeoutMs))
                    {
                        SynapseLog.Error("queue", "Request timed out in queue worker.",
                            request.Mod?.ModId);
                        SynapseGameComponent.Enqueue(() =>
                            request.Callback?.Invoke(
                                ChatResult.Failure("Request timed out.")));
                    }
                }
                catch (Exception ex)
                {
                    SynapseLog.Error("queue", $"Queue worker error: {ex.Message}",
                        request.Mod?.ModId);
                    SynapseGameComponent.Enqueue(() =>
                        request.Callback?.Invoke(
                            ChatResult.Failure($"Queue error: {ex.Message}")));
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        /// <summary>
        /// Recalculate throttle level based on recent request durations and queue depth.
        /// Called once per rate-limit window.
        /// </summary>
        private static void UpdateThrottle()
        {
            lock (_recentDurations)
            {
                if (_recentDurations.Count == 0)
                {
                    _throttleLevel = 1.0f;
                    return;
                }

                // Average response time
                long sum = 0;
                foreach (var d in _recentDurations) sum += d;
                float avgMs = sum / (float)_recentDurations.Count;

                // Estimate load: if avg response > 10s and queue > 3, start throttling
                float loadFactor = (avgMs / 10000f) * Math.Max(1, _queue.Count);

                if (loadFactor > 2.0f)
                    _throttleLevel = 0.3f;  // Heavy throttle
                else if (loadFactor > 1.0f)
                    _throttleLevel = 0.6f;  // Moderate throttle
                else if (loadFactor > 0.5f)
                    _throttleLevel = 0.8f;  // Light throttle
                else
                    _throttleLevel = 1.0f;  // Full speed

                if (_throttleLevel < 1.0f)
                {
                    SynapseLog.Info("queue",
                        $"Throttle updated: {_throttleLevel:F2} (avg response: {avgMs:F0}ms, " +
                        $"queue: {_queue.Count}, load factor: {loadFactor:F2}).");
                }
            }
        }

        /// <summary>
        /// Shutdown the worker thread.
        /// </summary>
        internal static void Shutdown()
        {
            _shutdown = true;
            _signal.Set();
        }
    }
}
