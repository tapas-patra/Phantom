# Phantom Engineering Docs

This folder captures the original Windows engineering structure. Product-wide specifications may cover both desktop platforms; the root [`PHANTOM-LIVE-COPILOT-IMPLEMENTATION-PLAN.md`](../../PHANTOM-LIVE-COPILOT-IMPLEMENTATION-PLAN.md) is the canonical entrypoint for the cross-platform live-copilot work.

Documents:
- `01-system-design.md`: product goals, domain boundaries, key flows, and non-functional rules
- `02-system-architecture.md`: local/hosted architecture, data ownership, schemas, and module boundaries
- `03-implementation-plan.md`: phased rollout plan for this repo with low-risk migration steps
- `04-web-platform.md`: public website and user dashboard architecture
- `05-pricing-and-billing.md`: final pricing model, credit semantics, and continuation rules
- `06-implementation-handoff.md`: confirmed decisions and implementation start scope
- `07-repo-split-plan.md`: repository split plan
- `08-current-root-ownership.md`: current root ownership boundaries
- `09-rag-latency-and-human-answering.md`: RAG latency and spoken-answer guidance
- `10-production-rag-implementation.md`: production hosted hybrid retrieval plan
- `11-live-copilot-optimization-plan.md`: cross-platform adaptive live-copilot architecture, Standard/Desi delivery, synthesis, cross-system logging, and Windows/macOS build work packages

Default scope for documents 01–10:
- Windows-first unless a document says otherwise
- local-first runtime
- hosted auth, billing, entitlements, and Premium knowledge features
- incremental refactor, not a rewrite

Document 11 is explicitly cross-platform for Windows and macOS.
