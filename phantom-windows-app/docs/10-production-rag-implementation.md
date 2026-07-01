# Production RAG Implementation

## Goal

Upgrade the hosted knowledge base from text-plus-JSON scoring to a production-grade Supabase/Postgres RAG pipeline with:
- real vector embeddings
- hybrid lexical + semantic retrieval
- versioned embedding metadata
- chunk indexing state
- reindex support without chat-model coupling

## Core Principle

The interview answer model and the retrieval embedding model are separate systems.

- chat model can change frequently
- embedding model should change rarely and only through a versioned migration path

## Target Storage Model

### `hosted_knowledge_bases`
- existing metadata
- `embedding_model`
- `embedding_version`

### `hosted_kb_documents`
- existing metadata
- `extracted_text`
- `content_sha256`
- `embedding_model`
- `embedding_version`
- `indexed_at_utc`

### `hosted_kb_chunks`
- existing metadata
- `section_title`
- `content_sha256`
- `metadata_json`
- `embedding vector`
- `embedding_model`
- `embedding_version`
- `indexed_at_utc`

## Embedding Strategy

### Versioned profile

The backend owns one active embedding profile:
- provider id
- model id
- dimensions
- version

The first source of truth is the admin dashboard persisted config. Env vars are only bootstrap defaults until an admin saves a profile.

All new chunks use the active profile. Search queries use the same active profile.

### Migration strategy

If embedding price or provider changes:
1. update the active embedding profile
2. new uploads use the new profile
3. old documents remain searchable with lexical fallback until reindexed
4. trigger KB reindex in the background or on demand

This avoids code rework when chat models change and limits operational work when embedding models change.

Current storage supports variable embedding dimensions, so provider/model switches no longer need a fixed-width vector schema. They should still always be paired with a version bump plus reindex.

## Ingestion Pipeline

1. Extract text from supported files.
2. Preserve structural breaks where possible.
3. Build semantic-ish chunks using paragraph-aware chunking with overlap.
4. Generate embeddings in batches.
5. Store chunk text, metadata, and vectors in one replacement transaction.
6. Mark document and KB indexing metadata as ready.

## Retrieval Pipeline

1. Embed the query with the active embedding profile.
2. Run keyword candidate retrieval with full-text search.
3. Run semantic candidate retrieval with `pgvector`.
4. Fuse the ranked sets.
5. Rerank the fused candidates in the backend using:
- reciprocal-rank fusion score
- semantic similarity
- lexical rank
- exact query-term overlap bonus
6. Return only the top few grounded snippets.

## Failure Strategy

### Upload/index failure
- fail the upload request instead of silently creating a partially indexed KB
- persist document error state when possible

### Search-time embedding failure
- fall back to lexical-only retrieval
- do not block the user from getting an answer

## Reindex Support

Add a KB reindex endpoint that:
- reloads stored `extracted_text`
- rebuilds chunks
- regenerates embeddings with the active profile
- atomically replaces document and chunk rows

This is the main operational path for embedding-version upgrades.

## Supabase/Postgres Design Choices

- `vector` extension enabled in migrations
- HNSW cosine index for semantic search
- GIN full-text index for keyword search
- per-KB scoping in every query
- short-lived in-memory search-result cache in the backend

## Rollout Sequence

1. Ship schema evolution and embedding service.
2. Index all new uploads with vectors.
3. Keep lexical fallback for old rows.
4. Reindex existing KBs when users or ops trigger it.
5. Once old rows are mostly migrated, rely on hybrid retrieval by default.
