---
id: procedure.system.application-candidate.intent-update
category: system
name: Application candidate intent update
governs: System capability system.application-candidate.intent-update
status: active
createdBy: "system"
changeNote: "Documents the bounded intent-association authoring capability."
---

## Description
Intent update stages a new inert application candidate that changes only the explicit `Matches` phrases of one exact current procedure or mechanic. Supply the exact target revision and content fingerprint, a stable idempotency key, and the current candidate identity when revising an existing candidate. An empty phrase list disables the alternate association. The selected current standing grant must carry both Read and Author authority.

## Matches
add alternate intent phrases
improve feature discovery without duplicating an action
disable a procedure alias
teach the system another way to ask

## Instructions
1. Choose a current procedure or mechanic returned by authorized discovery and preserve its exact ID, kind, revision, and content fingerprint.
2. Supply the complete desired phrase set. The operation replaces that target's alternate phrases; it does not add another implementation or publish the candidate.
3. Inspect the returned candidate, then use the existing validate and activate capabilities with their exact retained references. Only successful activation changes discovery.
4. If the target or candidate is stale, refresh both references before retrying. Reuse the same idempotency key only for the same logical update.

### Input and result
The closed input is `applicationId`, nullable `candidateId`, `expectedCandidateRevision`, exact target
`definitionId`, target `kind` (`procedure` or `mechanic`), positive revision, uppercase content
fingerprint, and the complete replacement `matchPhrases` array of at most 32 phrases.

```json
{"applicationId":"example","candidateId":null,"expectedCandidateRevision":0,"target":{"definitionId":"example.procedure.sample","kind":"procedure","revision":1,"contentFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"matchPhrases":["run the sample workflow","start sample processing"]}
```

Success is `committed` with the new inert candidate's operation receipt. Inspect that candidate before
validation. Changed discovery appears only after successful activation and derived retrieval refresh;
lexical discovery remains available if embeddings are unavailable.

## Constraints
- This editor changes only the explicit `Matches` section. It cannot change instructions, schemas,
  implementation, identity, or dependencies.
- Duplicate or whitespace-equivalent phrases are rejected. An empty array deliberately disables all
  alternate phrases for the target.
