# D&D code-adoption Slice 11E receipt — Temporary HP and healing decision

Date: 2026-08-26  
Status: **accepted**

## Accepted boundary

- Selected archived Feature 16 as the next dependency-ready Slice 11 family.
- Fixed exact SRD 5.2.1 Healing and Temporary Hit Point semantics at PDF pages 17–18.
- Reused current HP, mitigation, root action, typed-effect, transaction, replay, and audit owners.
- Approved recovery of the archived Temporary HP ID/schema/writer and adaptation of bounded healing.
- Kept recipient choice explicit and prohibited caller-supplied derived results.
- Recorded the direct-runner event limitation; unsupported archived healing/damage events are not
  activated, while result/effect audit data remains complete.
- Deferred Long Rest expiry to the separate rest family and all dying consequences to the dying
  family.

## Evidence

- Official PDF SHA-256:
  `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`.
- Foundry reference commit: `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
  `module/documents/actor/actor.mjs` lines 742–807.
- Archived Feature 16 receipts and tests were used only as first-party recovery evidence.

No runtime catalog, database, live campaign, public operation, or C# rule owner changed in 11E.
