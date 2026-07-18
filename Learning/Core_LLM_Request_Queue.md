# Concurrent LLM Request Queue

RimSynapse-Core powers the entire suite of RimSynapse mods through a highly optimized, asynchronous LLM Request Queue.

## Asynchronous Concurrency
RimWorld is a single-threaded game. To prevent LLM API calls from freezing the game, RimSynapse-Core utilizes a robust asynchronous request queue.
- **Worker Loop**: A background thread continuously monitors a priority queue of incoming LLM requests (from Faction generation, backstories, opportunistic tasks, etc.).
- **Parallel Dispatch**: The queue dispatches LLM HTTP requests asynchronously to the ThreadPool. 
- **User-Defined Scaling**: Users can configure the `Max Concurrent Requests` in the mod settings, allowing powerful local models (via LM Studio or Ollama) or remote APIs to process multiple LLM tasks simultaneously without bottlenecking.

## Priority Tiering and Dynamic Scoring
The queue uses a strict Priority system to ensure critical gameplay elements are generated before background flavor text:
1. **Tier 1 (Highest)**: Real-time interactions, chat responses.
2. **Tier 2**: Important immediate events.
3. **Tier 3**: Faction leader generation, major backstories.
4. **Tier 4-5 (Lowest)**: Opportunistic background tasks, profile evaluations.

### Dynamic Scoring Formula
To prevent queue starvation (where a massive influx of low-priority tasks prevents anything from happening), the queue calculates a Dynamic Score for every task:
`Score = (Priority * 100,000) + CappedAgeInTicks - TokenPenalty`

**Token Penalty**: To favor efficient tasks during opportunistic scheduling, RimSynapse tracks the Exponential Moving Average (EMA) of token usage per `requestName`. The Opportunistic Task Manager divides the raw opportunistic weight by a Token Penalty `(EMA / 100)`. Thus, heavy generation tasks (like stories) run less frequently than cheap tasks (like profile evaluations) during idle time.

This ensures that a very old, very short background task can eventually out-prioritize a newer, massive request, keeping the AI workflow smooth and responsive.
