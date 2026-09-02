---
type: "query"
date: "2026-09-02T03:35:30.297903+00:00"
question: "have you reviewed the KB part how it's designed and do we need any improvement on that side to accomodate this?"
contributor: "graphify"
outcome: "useful"
source_nodes: ["KnowledgeBaseEmbeddingService", "RetrievedContextSnippet", "HostedKnowledgeBaseProfileCardDto", "ResponsePlan"]
---

# Q: have you reviewed the KB part how it's designed and do we need any improvement on that side to accomodate this?

## Answer

Expanded from graph vocab: [knowledge, base, document, snippet, embedding, extract, structured, project, profile, experience, source, retrieval, section, metric, evidence]. Review: current KB has document sections, embeddings, hybrid retrieval, scoped results, and structured extraction/profile/project helpers. It still behaves primarily as chunk retrieval. To support grounded interview answers, preserve raw documents and add source-linked, user-verified candidate evidence cards; retrieve cards before chunks; return claim provenance; keep universal knowledge separate; add ingestion and grounding evaluations. Do not add a graph database or replace current vector search.

## Outcome

- Signal: useful

## Source Nodes

- KnowledgeBaseEmbeddingService
- RetrievedContextSnippet
- HostedKnowledgeBaseProfileCardDto
- ResponsePlan