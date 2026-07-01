# RAG Latency And Human Answering

## Goal

Keep interview responses fast when a hosted knowledge base grows, while making answers sound like a real candidate instead of a generic AI assistant.

## Immediate Direction

The current backend now uses a two-stage retrieval path:
- PostgreSQL full-text search to generate a small lexical candidate set
- lightweight in-app reranking on that candidate set

This is the right near-term step because it avoids a full chunk scan on every query without forcing a vector-database migration yet.

## Recommended Production Pattern

For larger Premium knowledge bases, the target architecture should be:

1. Ingestion pipeline
- parse uploaded files asynchronously
- chunk by semantic boundaries, not only fixed character windows
- create embeddings once during ingestion
- store document metadata needed for filtering

2. Retrieval pipeline
- first-pass retrieval with hybrid search
- combine lexical search and vector search
- apply metadata filters before reranking
- rerank only the top candidate set, not the whole corpus

3. Response pipeline
- retrieve only the top few grounded snippets
- pass a stable interview-type prompt plus a small style layer
- keep output short and spoken, not essay-like

4. Performance controls
- cache repeated KB searches for a short TTL
- cache stable prompt prefixes where the model/provider supports it
- stream the answer as soon as generation begins
- keep retrieval and prompt assembly in one backend request

## Market-Standard Choices

Teams with larger RAG systems typically use some variation of:
- hybrid retrieval instead of dense-only retrieval
- ANN vector indexes such as HNSW when semantic search is needed at scale
- metadata filtering and tenant scoping before reranking
- small candidate pools for rerankers
- prompt caching and request consolidation to reduce tail latency

## Human Answering Rules

Interview answers should be controlled by two layers:

1. Round type
- technical
- HR or behavioral
- system design
- coding
- product or case
- general

2. Delivery style
- spoken, first-person, concise
- one concrete example or analogy when useful
- no textbook intros, no corporate filler, no synthetic transitions
- answer as if the candidate is speaking live for 20 to 60 seconds

## Next Step If KB Limits Increase

If Premium KB size grows materially beyond the current cap, move from SQL candidate generation plus local rerank to:
- pgvector in PostgreSQL, or
- a dedicated vector store with hybrid retrieval and reranking

That migration should preserve:
- per-user isolation
- metadata filters
- short-lived search-result caching
- the same interview-type style layer already used by the desktop client
