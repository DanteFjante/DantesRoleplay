# D&D code-adoption Slice 13 design — archive disposition and recovery

Status: **accepted retained scope**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), archive-maintenance lane  
Dependency tree: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 13  
Ruleset alignment: `ruleset-neutral`; no D&D rule behavior is changed  
Outcome: determine the exact retained uses of `old-dnd/`, make an explicit retain/remove disposition,
and prove the active product builds independently while retained recovery evidence stays usable.  
Exclusions: editing archived content in place, bulk restoration, runtime/catalog changes, migrations,
public operations, and any removal without a separately satisfied destructive gate.

## Leaf schedule

| Leaf | Boundary | Exit evidence |
| --- | --- | --- |
| 13A | deterministic tracked-file and consumer inventory | accepted |
| 13B | exact disposition | accepted: retain all, remove none |
| 13C | clean-build and recovery evidence | accepted |
| 13D | parent closure | accepted |

## Safety boundary

`old-dnd/` is outside active catalog globs and project compilation. It may still be an input to
development-time provenance, transformation, classification, or recovery checks. “Not required at
runtime” is therefore not evidence that deletion is safe.

The existing explicit user decision to keep old D&D implementation permits a retain disposition.
It does not authorize deletion. If 13A finds any active evidence consumer—or if no removal is
useful—13B may close as retained with no destructive action. Removal requires an exact target list,
replacement evidence for every consumer, and separate confirmation.

## Parent acceptance

Parent 13 is accepted in retained scope. Every tracked archive file is fingerprinted and
classified, every non-archive consumer is reported, active builds/catalog do not consume archive
runtime code, accepted recovery/transformation checks remain reproducible, and no archive file was
modified or removed.
