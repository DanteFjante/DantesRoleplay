# Caldris playable opening dependency plan

Status: **lowest leaf ready**
Ruleset alignment: `dnd2024-compatible`
Source: not applicable; this slice authors narrative state and presentation and does not implement a D&D rule

## Outcome and non-goals

The Dungeon Master can run the first three sessions of *The Thirteenth Bell*, browse all forty-eight
prepared adventures from Campaign → Quests, and use reviewed portraits, scene art, and an item
handout. This leaf does not add numerical stat blocks, decide checks or DCs, automate rewards, or
claim lifecycle state the generic host cannot store.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Caldris World and campaign | `dnd2024-main` application World state | verified | Slice 3 receipt and live browser readback |
| Prepared adventures | GM-authorized World secrets Q01–Q48 | verified | 48 committed seeds and live Q01 search |
| Campaign continuity | campaign root, active arc, active chapter | verified | live `Volume I — Thirteen Bells` / `The Thirteenth Bell` |
| Campaign quest presentation | connected D&D 2024 web adapter | ready | currently projects only campaign goals into quest cards |
| Quest lifecycle | former specialized Quest owner | missing from generic host | generic host deliberately excludes legacy Quest tools and domain runtime |
| Session preparation | Caldris authored reference plus authorized knowledge | ready | existing World sync owner supports additive reviewed GM/open records |
| Visual artifacts | project-bound image assets | ready | image generation workflow and existing Caldris visual directory |

## Dependency tree

```text
Playable opening package [ready]
├─ live Caldris World/campaign [verified]
├─ Q01–Q48 GM adventure seeds [verified]
├─ quest-card projection from authorized seeds [ready]
├─ Sessions 1–3 authored packet and knowledge import [ready]
├─ portrait/location/item illustrations [ready]
└─ lifecycle-managed Quest records [blocked: owner absent from generic host]
```

## Conflicts and decisions

The accepted Quest contracts describe a former specialized runtime, but current source and the
generic host do not contain or register that runtime. Reintroducing it would be a separate platform
feature, not a Caldris content edit. This leaf therefore labels seed-derived cards `Active` only for
Q01 and `Prepared` for all others, and never invents objective completion, reward, or history.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Project Q01–Q48 into Campaign quests | authorized seed text | deterministic tests and no party-goal fallback regression |
| 2 | Author/import Sessions 1–3 and handouts | Q01 and existing cast/places | preview, identical commit, live readback |
| 3 | Generate reviewed visual packet | established descriptions | inspected project-bound files and hashes |
| 4 | Lifecycle runtime restoration | platform design | separate confirmed dependency plan; excluded here |

## Lowest ready leaf

Use only already-authorized knowledge entries whose title matches `Q01`–`Q48`. Parse their bounded
editorial fields for display, preserve the full seed as DM context, and fall back to existing party
goals when no seeds are available. No mutable quest state is created.

## Confirmation gates

The user's instruction to continue implementing the named remaining work confirms this Campaign UI
behavior, the new Session 1–3 reference IDs, and project-bound visual assets. No schema, migration,
catalog mechanic, D&D rule, or generic public protocol kind is introduced.

## Planning receipt

- Runtime artifacts created while planning: none.

