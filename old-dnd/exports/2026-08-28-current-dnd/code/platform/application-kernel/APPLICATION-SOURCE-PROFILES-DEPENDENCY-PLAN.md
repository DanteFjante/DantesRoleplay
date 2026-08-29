# Application source profiles dependency tree — selectable pre-campaign extensions

Status: **Slices 0–1 and Slice 2A optional legacy equipment accepted**  
Ruleset alignment: **ruleset-neutral**  
Source: not applicable; this is generic application infrastructure  
Owner/roadmap: [Application kernel](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), consumed by the
[D&D 2024 roadmap](../../ruleset/dnd2024/ROADMAP.md)

## Outcome and non-goals

Allow an operator to preview and activate an exact subset of registered application sources before
creating a campaign. The resulting activation fingerprint must preserve the selected profile, and
existing non-empty state spaces must retain their exact profile unless a separately reviewed
migration is performed.

This tree does not define D&D content, execute extensions during selection, make untrusted content
trusted, mutate an existing campaign, or add a second state/transaction authority.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Immutable source registrations | `source-registry` | verified | IDs, trust, precedence, roots, and fingerprints already persist |
| Candidate overlay preview | `application-preview` | verified exact-subset support | preview hashes only the selected registered sources and effective documents |
| Activation history | `application-activation` | verified exact-subset support | immutable activation revisions retain exact selected source/document manifests |
| Campaign binding | `state-space-administration` | verified | state spaces bind exact activation fingerprints; non-empty upgrades require migration |
| Public selection | query/commit and system capabilities | verified | callers can preview and activate a canonical exact source subset |

## Dependency tree

```text
Selectable pre-campaign application profile                         [Slice 0 accepted]
├── exact registered-source selection and validation                [verified]
├── selection-bound preview fingerprint                             [verified]
├── selection-bound activation request/replay/audit                 [verified]
├── public preview and activation sourceIds                         [verified]
├── exact state-space activation binding                            [verified prerequisite]
└── D&D core/extension authoring policy                              [confirmed]
```

## Conflicts and decisions

- `sourceIds: null` retains the existing all-registered-sources behavior for compatibility.
- An explicit `sourceIds` array means exactly that set; it is bounded, unique, registered, and
  canonicalized by ordinal source ID.
- The generic kernel does not know that `dnd2024-core` is special. D&D setup and authored policy
  must always include it; optional source IDs are additions to that core set.
- Unselected sources contribute no documents, problems, or fingerprints to the candidate profile.
- A state space continues to store only the exact activation fingerprint. Immutable activation
  history retains the selected source IDs behind that fingerprint.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 0 | Exact source-subset preview and activation | existing source, preview, activation, and state-space owners | **accepted** — core-only and core-plus-extension profiles differ deterministically and bind separate campaigns ([receipt](receipts/APPLICATION-SOURCE-PROFILES-SLICE-0-RECEIPT.md)) |
| 1 | Extension catalog packaging | leaf 0 | **accepted** — `dnd2024-extension.legacy-equipment` registers outside core and remains opt-in ([receipt](receipts/APPLICATION-SOURCE-PROFILES-SLICE-1-RECEIPT.md)) |
| 2 | Content-family schemas and records | leaf 1 | **Slice 2A accepted** — one hash-locked legacy rope definition is independently activatable and honestly extension-cited ([receipt](../../ruleset/dnd2024/adoption/evidence/DND-OPTIONAL-LEGACY-EQUIPMENT-SLICE-2A-RECEIPT.md)) |

## Confirmation gates

The user confirmed the source-selection public change, new permanent content IDs, and future schemas
provided SRD-faithful D&D 2024 remains core and every alteration/addition is an optional pre-campaign
extension. Migrations of non-empty campaigns and destructive archive changes remain separate gates.
