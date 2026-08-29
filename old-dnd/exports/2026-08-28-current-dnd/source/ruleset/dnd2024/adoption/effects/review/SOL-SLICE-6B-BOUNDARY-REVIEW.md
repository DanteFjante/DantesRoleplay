# Slice 6B boundary review packet — result/effect allowlist

Status: **ready for assigned Sol xhigh review**
Scope: ruleset-neutral, candidate-only adoption tooling.
Decision requested: approve the proposal-to-existing-generic-effect boundary before Slice 6C performs any live impact, transaction, replay, or rollback proof.

## Boundary to review

The allowlist accepts an exact result schema and converts only manifest-declared proposal kinds into
existing generic structural effect plans. It binds every component/relationship identity and every
role target in the manifest. Candidate output may provide only declared payload values. It cannot
choose an effect type, component type/version/schema hash, target entity, relationship kind, or
activation route.

## Evidence to inspect

- `contracts/result-effect-allowlist.schema.json` closes the manifest shape and generic effect vocabulary.
- `contracts/result-effect-allowlist.result.schema.json` is the neutral result-shape fixture; its hash is checked by the allowlist.
- `fixtures/result-effect-allowlist.valid.json` resolves the accepted Slice 6A mapping fixture by exact path, key, and SHA-256.
- `tools/Test-ResultEffectAllowlist.ps1` proves closed-schema, mapping-hash, role, proposal, payload, case-sensitivity, determinism, and no-write behavior.

## Required conclusions

1. Candidate proposal kinds are sufficient as the only candidate-controlled dispatch token.
2. The supported template fields do not let candidate output select identity-bearing kernel values.
3. Runtime conversion belongs in Slice 6C; this slice neither applies nor exposes effects.
4. Future D&D-owned mechanics must introduce a reviewed, source-aligned manifest rather than reuse this neutral fixture as game authority.

## Non-goals confirmed

No runtime host, catalog record, application activation, database record, public protocol, migration,
D&D formula, source/license decision, or permanent runtime ID is introduced here.
