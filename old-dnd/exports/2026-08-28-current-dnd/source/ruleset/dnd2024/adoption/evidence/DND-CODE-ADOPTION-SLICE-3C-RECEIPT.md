# D&D code-adoption Slice 3C receipt — parity and isolation proof

Status: **accepted 2026-08-25**
Implementation: [Slice 3C implementation](../../DND-CODE-ADOPTION-SLICE-3C-IMPLEMENTATION.md)
Parent: [Slice 3 design](../../DND-CODE-ADOPTION-SLICE-3-DESIGN.md)

## Delivered boundary

Completed the test-only Slice 3 adapter proof. A closed neutral manifest runs the accepted wrapper
and retained Feature 1 raw-check source in separate Jint instances, normalizes only declared shared
result pointers, and requires exact parity. It covers all six ability IDs; scores 1, 8, 10, 16,
and 30; DC below, equal to, and above the total; replay; changed seed; and seeded natural 1 and 20.

No discrepancy, source correction, or intentional difference was found. Foundry and the standalone
donor remain review evidence only and are not runtime or test dependencies.

## Artifact fingerprints

| Artifact | SHA-256 |
| --- | --- |
| `ability-check.parity.probe.json` | `471B5F6BC6673EA2F15D88AEFFC641C7F656C4CEE0DF2BCE2A592E4D19F09AB7` |
| `ability-check.parity.probe.schema.json` | `62D0BCFF1B87E9F5621391ACB13AC6C08D2308D7375C58D2DD828EF8C4F24165` |
| `operation-view-mutation.probe.js` | `3C84CA64E57AEF092EAFB8C3A6EF254FC0BFE8DAA9FAE20AC0E82C98A0DE291B` |
| corrected `ability-check.wrapper.js` | `2E56030E3CE18A3DABB88CCEE0844A215AC8D8BF80CBF7DE1678465ECB6EFEC3` |
| corrected `ability-check.wrapper.probe.json` | `EA90D106EC674C5AE30309E38EC4DB6FD60A91EA2A6D92E487CBD314FECA4563` |
| corrected wrapper provenance | `03650F9BAC047AA34F087DEAECCDB6ACFC15EC586ECC6CF42A13997B37B2F480` |
| `ApplicationAdoptionProbeTests.cs` | `8F79CAC0722F2311E91F5579C7623589BF81AB038C1E1575382EB31BE9A1DC61` |
| `JintMechanicEngine.cs` | `BE0079715022FCA42DD84047289DE3D030C846CAE99A1352A14E5395A581C6E8` |
| `SandboxTests.cs` | `F1DA1A87DCDB3A246D3361FA64024297E098247C70B4BA16369D3312843EC16C` |
| retained Feature 1 comparator record | `72A00C9EC23EEA15FA41E5C816B6A702159BD820C761F4067FCB601E7CF05F86` |

## Problems found and fixed

1. Projected roles, role components, and action input reached JavaScript as mutable objects even
   though the projection contract describes frozen context. `JintMechanicEngine` now deep-freezes
   both roles and parsed input. A generic sandbox test and the mutation probe prove assignments are
   rejected and values stay unchanged.
2. The wrapper result schema capped DC at JavaScript's maximum safe integer, but the wrapper accepted
   larger integers. The wrapper now rejects DC above `9007199254740991`, with a negative vector and
   corrected provenance hash.

## Verification evidence

- Focused adoption plus sandbox tests: **29 passed, 0 failed**.
- Full solution: **20 LocalAI tests and 892 main tests passed, 0 failed**.
- AJV validation passed for operation-view, wrapper, parity, and provenance artifacts; every D&D
  adoption JSON file parsed.
- Adoption contract validation: 2 schemas compiled, 2 positives accepted, 9 negatives rejected.
- Catalog validation: **144 records valid** with 21 existing near-duplicate warnings; no live data
  touched.
- Same state/input/seed yields byte-identical candidate data. Alternate seed changes the result and
  still matches the retained source. Candidate, retained source, and mutation probe return zero
  effects, events, and notifications, and their source hashes are unchanged after execution.

## Deliberate exclusions and exit

Slice 3 created no permanent application/source/component/projection/mechanic ID, catalog record,
public operation, migration, activation, transaction, donor state, or persistent game data. The
wrapper remains a development-only candidate. Slice 4 reusable conformance tooling is the next
planned parent; promoting a D&D mechanic remains a separately confirmed feature slice.
