# D&D code-adoption Slice 12C implementation — attribution and pinned-upstream review

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), adoption maintenance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 12C  
Ruleset alignment: `ruleset-neutral` maintenance tooling  
Source ID and locator: not applicable; no rule behavior changes  
Outcome: audit the accepted attribution/pinning boundary and produce a bounded diff from each exact
pinned donor commit to its current evidence branch for human review only.  
Exclusions: updating pins, importing code/content, executing donor code, submodule initialization,
automatic activation, runtime/catalog changes, and live data.  
Allowed files/areas: this document; adoption maintenance tools/contracts/tests; review evidence;
Slice 12C receipt.  
Stop point: offline tests and attribution audit pass, a current upstream report is recorded, and no
lock/runtime file changes.

## Confirmed decisions and prerequisites

- `donor-lock.json` remains the only donor/revision owner and retains exact 40-character commits,
  no floating runtime refs, no production dependency, and no automatic activation.
- `THIRD-PARTY-NOTICES.md` remains the attribution owner.
- Foundry dnd5e remains engineering-reference-only. A changed branch may request review but can
  never become an import candidate through this workflow.
- An upstream difference is expected maintenance information, not a validation failure and not
  permission to alter the lock.

## Report behavior

For each locked source the tool resolves either an explicitly supplied candidate commit (for
offline/reproducible tests) or exactly `refs/heads/<branchEvidence>`. In a unique OS-temporary Git
repository it fetches only the pinned and candidate commits, verifies both as commits, compares
tree IDs, lists bounded name/status changes, and compares Git blob IDs for every configured
fingerprint. The report records `unchanged` or `review-required` per source and always records:

- `automaticActivation: false`;
- `lockChanged: false`;
- `runtimeWrites: "none"`; and
- whether human review is required.

The tool never checks out a working tree, initializes submodules, runs package commands, or edits
the donor lock. Cleanup is allowed only for its verified unique child below the operating-system
temporary directory.

## Attribution audit

The audit requires the exact SRD and CC BY 4.0 URLs, the locked MIT donor commit and copyright/
license notice, exact nonfloating pins, the Foundry reference-only role, and provenance constraints
that prevent an accepted Foundry reuse entry. It rejects policy drift and missing notice evidence.

## Failure contract

Malformed locks/candidate maps, duplicate keys, unsafe branches, nonexact commits, missing required
fingerprints, ambiguous remote refs, Git failures, an output path equal to the lock, or cleanup
boundary failure stop without a success report. Network failure does not alter the last accepted
report or any runtime artifact.

## Acceptance matrix and verification

| Case | Expected |
| --- | --- |
| Same pin/candidate | `unchanged`, zero changed paths |
| New candidate | `review-required`, bounded sorted path and fingerprint changes |
| Bad candidate/map | fail closed; no success report |
| Attribution drift | audit failure |
| Lock immutability | byte hash identical before/after every run |
| Network comparison | report only; no checkout, execution, import, or activation |

The offline workflow tests, attribution audit, and current upstream comparison are recorded in
`adoption/evidence/DND-CODE-ADOPTION-SLICE-12C-RECEIPT.md`. The leaf stops before Parent 12
acceptance changes.
