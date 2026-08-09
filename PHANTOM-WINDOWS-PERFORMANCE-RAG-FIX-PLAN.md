# Phantom Windows Performance and RAG Fix Plan

## Objective

Turn Phantom from a generic chat client into a low-latency interview assistant while preserving candidate grounding and existing authentication, billing, lock, telemetry, and BYO-provider behavior.

This plan is an implementation handoff for Codex. It covers only:

- `phantom-windows-app/`
- `phantom-windows-app-backend/`

Do not modify:

- `phantom-dashboard-backend/`
- `phantom-website-dashboard/`

## Required working rules

1. Read the root `AGENTS.md` and `phantom-windows-app/AGENTS.md` before editing.
2. Preserve existing user changes and ignore unrelated files such as `.DS_Store`.
3. Work in the phase order below. Do not start agentic/product expansion before the latency and RAG correctness phases pass.
4. Prefer deletion or reuse of current code over a new framework.
5. Do not add an agent SDK, vector database, Redis, message bus, or reranking model.
6. Keep desktop BYO and managed-provider paths working.
7. Do not change authentication, billing, credit, lock, or entitlement semantics.
8. Add the smallest runnable check for each new non-trivial routing or retrieval rule.
9. Build the touched project after every phase. Do not wait until the end to discover contract drift.
10. Do not claim Windows runtime validation unless it was run on Windows with WebView2.

## Target request behavior

The normal live paths should become:

```text
Generic question
  -> deterministic local route
  -> one answer-model request
  -> immediate incremental rendering

Profile question
  -> session-cached profile card
  -> one answer-model request
  -> immediate incremental rendering

Project overview/follow-up
  -> active/named project card
  -> one answer-model request
  -> immediate incremental rendering

Deep candidate-specific detail
  -> hard-scoped hybrid retrieval
  -> one answer-model request
  -> immediate incremental rendering
```

The live path must not normally contain a planner-model request, resume summarization request, job-description summarization request, provider-catalog refresh, or full transcript rebuild.

## Performance measurements and initial SLOs

Record these timestamps for every live request:

```text
send_clicked
context_ready
retrieval_started
retrieval_finished
provider_request_started
provider_headers_received
first_upstream_token
first_backend_sse_write
first_desktop_chunk
first_ui_paint
response_completed
```

Initial engineering targets:

| Measurement | Target |
|---|---:|
| Desktop UI event handling p95 | < 50 ms |
| Generic send-to-provider-dispatch p95 | < 150 ms |
| Personalized send-to-provider-dispatch p95 | < 400 ms |
| Retrieval p95 when required | < 500 ms |
| Voice-final-to-send p95 | < 400 ms |
| First visible token p50 | < 700 ms |
| First visible token p95 | < 1.5 s |
| Streaming paint p95 | < 30 ms |
| Short answer completion p95 | < 5 s |

These are targets, not claims. Report actual p50/p95 values after Windows testing.

---

## Phase 0: Establish a baseline

### Goal

Separate provider latency from Phantom-controlled latency before changing behavior.

### Files

- `phantom-windows-app/MainWindow.xaml.cs`
- `phantom-windows-app/Services/ConversationManager.cs`
- `phantom-windows-app/Services/HostedManagedAiService.cs`
- `phantom-windows-app-backend/Services/ManagedAiService.cs`
- existing logging/telemetry code only as needed

### Steps

1. Add one request correlation ID at the desktop send boundary.
2. Use `Stopwatch.GetTimestamp()` or `Stopwatch` for monotonic elapsed times.
3. Log the milestones listed above with the correlation ID.
4. Record route type, provider, model, voice/typed, image/no-image, retrieval/no-retrieval, input-token estimate, output length, cancellation, retry, and error outcome.
5. Do not log resume text, job-description text, retrieved content, access tokens, API keys, or full user questions.
6. Keep instrumentation non-blocking enough that it does not become the measured bottleneck.
7. Create a baseline question set containing at least:
   - 10 generic technical questions
   - 10 behavioral/profile questions
   - 10 project questions
   - 10 project follow-ups
   - 10 system-design questions
   - 5 coding questions
   - 5 voice questions
   - 5 screenshot questions
8. Record cold and warm runs separately.

### Exit criteria

- A single request can be traced from click to final token.
- Logs distinguish routing, retrieval, provider TTFT, desktop receipt, and first paint.
- Baseline p50/p95 values are saved for comparison.
- No sensitive candidate content is written to the new timing logs.

---

## Phase 1: Remove hidden serial model work

### Goal

Make the normal path one answer-model call and expose its stream immediately.

### Primary file

- `phantom-windows-app/Services/ConversationManager.cs`

### Steps

1. Trace both `SendMessageAsync` and `SendMessageStreamAsync` callers before editing.
2. Replace `PlanResponseAsync` on the live path with a synchronous deterministic router.
3. Reuse the existing profile and project heuristic methods.
4. Add only the missing retrieval rule for explicit uploaded-document questions, such as references to:
   - uploaded document
   - knowledge base
   - notes
   - resume details not present in the profile card
   - exact implementation/detail requests not present in a selected project card
5. Route an unrecognized question to `Direct`. Do not call a planner because the router is uncertain.
6. For a direct route, skip structured grounding and retrieval, then call the normal final answer method with the existing conversation context.
7. For profile and project routes, prepare structured context before considering vector retrieval.
8. Retrieve project snippets only when the selected structured field is absent or the question explicitly asks for deep implementation evidence.
9. Remove or leave unreachable the planner prompt, planner JSON parser, and buffered planner request. Delete them once all callers are gone.
10. Ensure cancellation propagates through routing, retrieval, and answer generation.

### Required router checks

Cover at least:

| Question | Expected route |
|---|---|
| “Explain dependency injection” | Direct |
| “Tell me about yourself” | Profile |
| “What are my strengths?” | Profile |
| “Tell me about my recent project” | Project |
| “Why did you choose that database?” with active project | Project / active project |
| “Show the exact deployment details from my notes” | Retrieve |
| “Give me an example” after a generic concept | Direct |
| A named known project | Project / named project |
| A named unknown project | Do not silently select an unrelated recent project |

### Exit criteria

- Generic questions begin streaming from the answer model without a planner call.
- Personalized questions use at most one retrieval operation and one answer-model call.
- Unknown project names do not silently become a different project.
- Existing image support, cancellation, retry, managed fallback, and BYO behavior still work.

---

## Phase 2: Move preparation out of the live request

### Goal

Never summarize a resume or job description after the interviewer has asked a live question.

### Files

- `phantom-windows-app/Services/ConversationManager.cs`
- resume/job-description import or settings callers discovered during tracing

### Steps

1. Find every setter/import path for resume and job-description content.
2. Trigger summarization when the content is added or changed.
3. Persist or retain the completed summary using the existing settings/context persistence mechanism.
4. Warm both summaries when an interview session starts.
5. Remove awaited resume/JD summarization from `SendMessageAsync` and `SendMessageStreamAsync`.
6. If a summary is unavailable during a live request:
   - use an existing structured profile card when available;
   - otherwise include a safely truncated raw fallback;
   - start/retry summarization in the background;
   - do not delay the current answer.
7. Invalidate only the summary whose source text changed.

### Exit criteria

- First-question timing contains no resume/JD model calls.
- Editing a resume or JD refreshes only its own cached summary.
- Failure to summarize does not prevent live answering.

---

## Phase 3: Make streaming rendering incremental

### Goal

Stop rebuilding all chat HTML and rerunning Mermaid every 50 ms.

### Files

- `phantom-windows-app/MainWindow.xaml.cs`
- `phantom-windows-app/MarkdownHelper.cs`

### Steps

1. Keep the current WebView2 chat shell.
2. Add the smallest JavaScript API required for streaming:

```text
beginAssistantMessage(id, label)
appendAssistantDelta(id, text)
finalizeAssistantMessage(id, html)
```

3. While streaming, append plain text only to the active assistant message.
4. Track the last rendered buffer position and send only the new substring on each timer tick.
5. Keep a 50–100 ms coalescing timer; do not dispatch every provider token separately.
6. At completion, render Markdown for only the completed message and replace that message's temporary plain-text content.
7. Run Mermaid only inside the completed message.
8. Use a full transcript render only for initial load, conversation restore, clear, or catastrophic renderer recovery.
9. Remove the per-token dispatcher activity update. Record first-token activity and then throttle long-stream updates.
10. Remove unnecessary `Task.Run` wrapping around naturally asynchronous network work.
11. Do not add transcript virtualization unless incremental rendering still fails the long-conversation test.

### Required Windows checks

- Stream a 500-token answer into an empty conversation.
- Stream a 500-token answer after at least 30 prior messages.
- Stream an answer containing code fences.
- Stream an answer containing a Mermaid diagram.
- Resize and move the overlay while streaming.
- Cancel mid-stream and immediately send another question.

### Exit criteria

- Prior message DOM nodes are not recreated during streaming.
- Mermaid is not invoked on every partial chunk.
- The overlay remains interactive throughout a long answer.
- First visible text appears as soon as the first coalesced chunk arrives.

---

## Phase 4: Remove desktop-side pauses and blocking work

### Goal

Remove predictable UI freezes that are independent of model speed.

### Voice steps

1. Replace the 1,500 ms voice completion timer plus 500 ms delay with final-result-driven sending.
2. Retain a configurable 250–400 ms correction debounce.
3. Cancel the pending send if new speech arrives.
4. Keep the final transcript visible immediately.
5. Measure recognition-final-to-send separately from provider TTFT.

### Screenshot steps

1. Move PNG encoding/base64 conversion off the UI thread.
2. Downscale screenshots that exceed the useful screen-text resolution.
3. Avoid re-encoding the same attachment on retries.
4. Preserve the original path only when the selected model/request genuinely needs it.

### Provider catalog steps

1. Stop calling `ByoProviderModelCatalogService.RefreshStaleCatalogs` synchronously during settings load.
2. Load the last cached catalog immediately.
3. Refresh stale catalogs asynchronously after the window is interactive or on explicit refresh.
4. Apply the same rule to `RefreshManagedCatalogCache` during window construction.

### Logging steps

1. Replace synchronous `Dispatcher.Invoke` with batched/asynchronous UI log updates.
2. Keep one file handle or one background writer instead of `File.AppendAllText` for each message.
3. Do not update the debug collection while the debug panel is closed.
4. Remove hot-path informational logs that occur for every tiny stream fragment.

### Exit criteria

- Voice-final-to-send p95 is below 400 ms on the Windows test machine.
- Settings/window startup never waits on external provider catalog HTTP calls.
- Screenshot preparation does not freeze input or window movement.
- Logging does not synchronously marshal every background event onto the UI thread.

---

## Phase 5: Fix RAG correctness before tuning scores

### Goal

Prevent wrong-project and stale-context answers.

### Files

- `phantom-windows-app/Services/ConversationManager.cs`
- `phantom-windows-app/Infrastructure/Context/HostedKnowledgeRetrievalService.cs`
- `phantom-windows-app/Infrastructure/Context/LocalKnowledgeRetrievalService.cs`
- `phantom-windows-app-backend/Services/HostedKnowledgeBaseService.cs`
- `phantom-windows-app-backend/Persistence/HostedKnowledgeBaseRepository.cs`

### Steps

1. Treat active-project and previous-document IDs as hard retrieval filters.
2. In backend hybrid search, set `restrictToPreferredDocuments` when scoped document IDs are supplied.
3. In local fallback, filter `pack.Documents` by the same IDs before scoring.
4. Preserve old snippets after an empty retrieval only when the requested document-ID set equals the previous snippet document-ID set.
5. Clear snippets when the active project changes.
6. Do not default an explicitly named but unknown project to the recent project. Return a grounded-miss response or request the exact project.
7. Ensure returned hosted snippets all belong to the authenticated user's KB and, when scoped, to the requested documents.
8. Keep structured profile/project instructions and snippets consistent; never combine one project's card with another project's snippets.

### Required checks

- Project A retrieval cannot return Project B chunks.
- Switching A -> B with a B miss cannot preserve A snippets.
- Previous-doc follow-up remains within those documents.
- Global retrieval can still search all documents.
- Hosted failure with local fallback respects the same scope.
- Unknown project target produces a grounded miss, not a recent-project answer.

### Exit criteria

- No wrong-project result in the RAG test corpus.
- Scoped retrieval contains only allowed document IDs.
- Empty scoped retrieval cannot reuse unrelated context.

---

## Phase 6: Optimize RAG latency and cache behavior

### Goal

Keep personalized retrieval bounded and predictable.

### Steps

1. Change the desktop KB summary cache from a 30-second timer to session lifetime plus revision invalidation.
2. Use `LastProcessedAtUtc` initially as the KB revision; add a dedicated monotonically increasing revision only if timestamp semantics prove insufficient.
3. Key interview context packs by KB revision, project/profile ID, and pack type.
4. Warm only profile, recent-project, and active-project packs required by likely interview questions.
5. Do not clear and rewarm packs unless the KB revision changes.
6. Include the KB revision in backend search cache keys so multiple backend instances naturally miss stale entries.
7. Do not normally cache a lexical-only result caused by embedding-provider failure, or give it a short degraded TTL.
8. Set live query-embedding retries to zero. Retain indexing retries.
9. Start with a 250–350 ms query-embedding timeout and a 500–700 ms total retrieval deadline.
10. On embedding timeout/failure, immediately use lexical retrieval.
11. Convert the hot hybrid-search database operation to async Npgsql only if concurrency measurements show thread-pool pressure.
12. Run lexical retrieval concurrently with query embedding only if Phase 0/6 measurements show embedding latency remains a material part of personalized TTFT.

### Exit criteria

- Warm repeated profile/project questions do not reload the KB summary.
- Embedding failure cannot delay retrieval for multiple seconds.
- Cache entries cannot survive a KB revision change.
- Retrieval timing reports cache hit, semantic/lexical/degraded mode, and candidate count.

---

## Phase 7: Improve RAG ranking with measured changes

### Goal

Improve retrieval recall without adding another model call.

### Steps

1. Create a checked-in RAG evaluation dataset with:

```text
question
expected route
expected profile/project ID
expected source document IDs
required facts
forbidden facts
```

2. Include broad questions, exact details, follow-ups, project switches, terminology mismatches, and deliberate no-answer cases.
3. Record recall@3, wrong-document rate, empty-result rate, irrelevant-snippet rate, and retrieval p50/p95.
4. Replace all-term `plainto_tsquery` behavior for expanded queries with a safe OR/minimum-match lexical query built from normalized meaningful terms.
5. Preserve title, section-title, preferred-document, semantic, and exact-term boosts.
6. Return or log internal diagnostic fields: chunk ID, section title, lexical score, semantic score, fused score/rank, and KB revision.
7. Tune `MinSnippetScore`, `MinSemanticSimilarity`, candidate multiplier, and snippet count only against the evaluation corpus.
8. Keep the maximum injected context small: normally 2–3 non-duplicate snippets.
9. Do not add an LLM reranker unless the measured retrieval metrics remain insufficient after deterministic tuning.

### Exit criteria

- RAG evaluation results before and after the ranking change are saved.
- Ranking changes improve recall or wrong-document rate without breaking latency targets.
- No threshold is changed solely by intuition.

---

## Phase 8: Stabilize backend streaming and model selection

### Goal

Remove request-time metadata refreshes, unsafe stream failover, and excessive output.

### Files

- `phantom-windows-app-backend/Services/ManagedAiCatalogService.cs`
- `phantom-windows-app-backend/Services/ManagedAiService.cs`
- relevant managed AI request contracts

### Steps

1. Validate provider/model/vision support against the last successful in-memory or database catalog snapshot.
2. Move external catalog refresh to a background timer or explicit admin action.
3. Do not call `EnsureCatalogFreshAsync().GetAwaiter().GetResult()` from a chat request.
4. Delay downstream SSE commitment until provider response headers succeed.
5. Permit credential failover only before the first downstream response byte.
6. After streaming starts, emit a defined SSE error and terminate rather than appending another provider's output.
7. Flush the first token immediately, then coalesce later provider deltas for approximately 25–50 ms.
8. Add no-buffering headers and verify the deployment proxy does not buffer SSE.
9. Replace the fixed 2,000-token live budget with question-class budgets:

| Class | Starting output budget |
|---|---:|
| Follow-up/clarification | 120–180 tokens |
| General/behavioral | 180–300 tokens |
| Personalized project answer | 220–350 tokens |
| Coding explanation | 300–500 tokens |
| System-design first pass | 400–600 tokens |
| Explicit deep expansion | Up to 1,000 tokens |

10. Preserve an explicit “expand/deeper” path rather than making every first answer long.

### Exit criteria

- Catalog refresh cannot block chat TTFT.
- Mid-stream failover cannot duplicate or concatenate answers.
- First token is not delayed by batching.
- Typical interview answers stay within their measured spoken-answer budget.

---

## Phase 9: Add interview session intelligence without a multi-agent live path

### Goal

Support follow-ups, system design, behavioral answers, and candidate continuity with deterministic state.

### Steps

1. Add one small session-state record owned by `ConversationManager`:

```text
round_type
question_class
active_topic
active_project_id
answer_depth
last_evidence_document_ids
last_answer_summary
```

2. Reuse the existing active-project behavior rather than building a general memory framework.
3. Update obvious state synchronously using deterministic rules.
4. Resolve “this”, “that”, “it”, “that design”, and “that project” against the active topic/project.
5. Add response shapes for:
   - behavioral/HR
   - technical explanation
   - system-design first pass
   - coding answer
   - project deep dive
6. Keep all common live answers to one model request.
7. Run optional answer coaching, likely-follow-up generation, and compact session-summary updates after the answer completes.
8. Never put coach analysis on the first-token critical path.
9. Extend the existing offline structured extraction to store evidence/source document IDs for profile and project fields where practical.

### Exit criteria

- Follow-ups retain the correct active project/topic.
- Switching topics resets the relevant state.
- System-design and coding questions receive appropriate first-pass structures without invoking a planner agent.
- Background coaching cannot delay or modify the current live stream.

---

## Phase 10: Consider a combined managed interview endpoint only if measured

### Goal

Remove the desktop -> KB backend -> desktop -> managed AI backend round trip if it remains material after the earlier fixes.

### Gate

Do not implement this phase unless measurements show the extra desktop/backend orchestration contributes meaningful p95 latency or creates correctness/security duplication.

### If the gate passes

1. Add a managed-only streaming endpoint that accepts question, interview/session state, optional active project ID, answer depth, and optional image.
2. Keep authentication, entitlement, lock, billing, and usage enforcement in the existing backend authority.
3. Perform structured lookup and optional scoped retrieval in the backend.
4. Reuse existing prompts and response budgets; do not create a second independent prompt system.
5. Stream the provider response through the existing SSE machinery.
6. Keep BYO mode on the desktop path.

### Exit criteria

- The combined endpoint produces the same or better grounding than the desktop-managed path.
- It measurably reduces p95 TTFT.
- No prompt or routing behavior is duplicated without a single source of truth.

---

## Phase 11: Ingestion and operational improvements

### PDF support

1. Add server-side PDF text extraction because resumes commonly arrive as PDFs.
2. Preserve page boundaries where possible.
3. Detect empty/scanned PDFs and report that OCR is required.
4. Do not add OCR until real usage requires it.

### Existing full-KB replacement

The current transactional delete/reinsert path is acceptable under the 1,200-chunk limit. Keep it unless profiling shows update transactions affect retrieval or the KB limits grow materially.

### Vector index

The current HNSW expression index targets the default 1,536 dimensions. Keep this design while the per-KB cap is small. Add dimension-specific operational indexes only when a non-default profile is deployed and query plans show sequential vector scans are a problem.

### Exit criteria

- Text-based PDFs can be indexed.
- Unsupported scanned PDFs fail with a clear message.
- `EXPLAIN ANALYZE` confirms the expected lexical/vector indexes for the deployed embedding profile.

---

## Final verification matrix

### Backend checks Codex must run

```bash
dotnet build phantom-windows-app-backend/Phantom.WindowsApp.Backend.csproj
```

Run any added retrieval/router checks and the RAG evaluation command. With a PostgreSQL/pgvector test instance available, verify:

- lexical-only search;
- semantic hybrid search;
- embedding timeout fallback;
- scoped document filtering;
- cache revision invalidation;
- reindex compatibility;
- concurrent searches for different users;
- no cross-user or cross-project results;
- SSE first token, cancellation, provider error, and failover boundary.

### Desktop checks Codex can perform statically/non-Windows

- inspect all modified callers;
- compile shared/non-Windows-compatible code where possible;
- report any Windows-targeting limitation explicitly;
- do not claim WebView2, voice, overlay, keyboard-hook, or cursor behavior was run.

### Final Windows tests for the owner

Build:

```powershell
cd phantom-windows-app
dotnet build SecureOverlay.sln -c Debug
```

Test:

1. Cold launch with configured BYO providers.
2. Cold launch with managed provider.
3. Generic typed question.
4. Profile question.
5. Named project question.
6. Active-project follow-up.
7. Switch to another project.
8. Unknown project name.
9. Deep project detail requiring retrieval.
10. Retrieval while embedding provider is unavailable.
11. Long conversation with 30+ messages.
12. Code and Mermaid streaming.
13. Voice auto-send.
14. Screenshot question.
15. Cancel and immediately resend.
16. Managed credential failure before streaming.
17. Provider failure after partial streaming.
18. Free/pro/premium entitlement and billing regression checks.
19. Session lock and usage reconciliation regression checks.

## Final handoff required from Codex

Codex should return:

1. Files changed, grouped by phase.
2. Behavior removed or simplified.
3. Tests/builds run with exact results.
4. Baseline versus final latency table.
5. RAG evaluation before/after table.
6. Known limitations and deferred gated work.
7. Exact Windows manual test checklist still requiring owner verification.
8. No claim that Windows-only behavior passed unless actually executed on Windows.

## Definition of done

The implementation is ready for owner testing when:

- generic questions use one answer-model call;
- first-question preprocessing no longer blocks the answer;
- streaming updates only the active message;
- voice no longer adds a fixed two-second wait;
- catalog refresh and logging do not block the hot path;
- scoped RAG cannot return or preserve another project's snippets;
- KB caches are revision-aware;
- embedding failure falls back within the live retrieval budget;
- backend stream failover cannot duplicate output;
- output budgets are question-class aware;
- the backend builds successfully;
- all non-Windows checks pass;
- remaining Windows/WebView2 validation is clearly handed to the owner.

## Recommended Codex execution model

Use `GPT-5.6 Sol` with `xhigh` reasoning for the implementation task. The work crosses WPF/WebView2, asynchronous streaming, .NET backend contracts, PostgreSQL/pgvector ranking, and regression-sensitive billing/session paths. Use one persistent Codex task and execute one phase at a time, validating after every phase.

Use `GPT-5.6 Terra` only for cheaper routine follow-up edits after the architecture, critical path, and RAG correctness changes are complete.

## Copy/paste prompt for the implementation task

```text
Implement PHANTOM-WINDOWS-PERFORMANCE-RAG-FIX-PLAN.md in phase order.

Scope only phantom-windows-app and phantom-windows-app-backend. Preserve all
authentication, billing, credit, entitlement, lock, telemetry, managed-provider,
and BYO-provider behavior. Do not touch either dashboard project.

Start by reading the root and desktop AGENTS.md files, inspecting git status, and
capturing the Phase 0 baseline. Complete the smallest high-impact implementation
for each phase, run its required checks/build, and record results before continuing.
Do not add an agent framework, new vector database, Redis, reranking model, or
other speculative infrastructure. Treat Phase 10 as gated and implement it only
if the measurements satisfy its gate.

Continue through every ungated phase that can be completed safely in this
environment. Do not stop merely because Windows runtime testing is unavailable;
finish static implementation and backend validation, then hand me the exact
Windows/WebView2 manual tests that remain. At the end, provide the handoff listed
in the plan, including baseline/final latency and RAG evaluation results where the
environment allowed them to be measured. Do not commit or push unless I ask.
```
