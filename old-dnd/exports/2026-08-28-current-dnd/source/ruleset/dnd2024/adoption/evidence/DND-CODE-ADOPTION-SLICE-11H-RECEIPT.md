# D&D code-adoption Slice 11H receipt — Temporary HP/healing family acceptance

Date: 2026-08-26  
Status: **accepted**

## Accepted family

- Canonical optional positive Temporary HP state and explicit grant/keep/replace/expire writer.
- Capped healing that never touches the buffer and avoids identical full-HP revisions.
- Weapon damage ordering of roll -> mitigation -> Temporary HP -> actual HP.
- One typed-effect transaction for buffer/HP changes with operation replay and existing generic
  rollback/audit ownership.
- Buffer absence compatibility, corruption rejection, closed caller input, and exact SRD source
  attribution.

## Consolidated verification

- Official SRD 5.2.1 PDF: 364 pages; SHA-256
  `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`; Healing p. 17 and Temporary
  Hit Points p. 18 inspected.
- Foundry reference-only review: commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
  `module/documents/actor/actor.mjs` lines 742–807.
- All **56** active D&D mechanic scripts passed syntax validation.
- Focused weapon/Temporary HP/healing tests: **7 passed, 0 failed**.
- Complete D&D test class: **91 passed, 0 failed**.
- Catalog: **144 valid records**, same **21 unrelated advisories**, no live data.
- Solution build: **0 warnings, 0 errors**.
- Shared suite: **1,116 passed, 0 failed**; Local AI: **21 passed, 0 failed**.
- Slice 11-scoped diff/whitespace audit passed; only repository line-ending notices appeared.

## Deliberate exclusions

No unsupported event type/output, Long Rest expiry, dying/death state, healing source, non-weapon
damage, migration, campaign rebinding, public operation, or C# D&D rule logic was introduced.
