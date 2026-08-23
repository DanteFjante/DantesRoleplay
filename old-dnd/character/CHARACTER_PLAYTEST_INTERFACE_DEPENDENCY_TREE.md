# Character playtest interface dependency tree — provisional recordable actors

Status: **implemented; acceptance blocked by existing full-suite failures**  
Ruleset alignment: **dnd2024-compatible**  
Source: Not applicable. This does not implement a D&D rule; it records declared playtest material
alongside existing D&D-owned state without granting, calculating, or executing it.

## Outcome and non-goals

Provide a short-term MCP-facing character setup interface for the first game:

1. use the existing `commit(kind: "effects")` operation to create a provisional actor and its
   supported mechanical state in one transaction;
2. use the existing C15 `commit(kind: "campaign")` operation to attach that actor to one active
   campaign; and
3. keep provisional class, background, spell, equipment, feature, trait, and GM-ruling records in
   one small versioned component that is safe to revise or later retire.

It is intentionally **not** CH3–CH6 character creation. It creates no governed origin/class
receipt, class membership, spellcasting entitlement/resource, item instance, equipment state,
derived value, trait behavior, or new MCP tool/kind. A label such as `Wizard` or `Bard` is a
playtest record only; the GM/AI adjudicates its missing behavior.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Atomic structural creation | `commit(kind: "effects")` / `procedure.world.change` | verified | Validates and applies one typed effect list atomically; it already creates the provisional base actors in `PLAYTEST_CHARACTER_BOOTSTRAP.md`. |
| Campaign participation | C15 `procedure.campaign.character-participation` | verified | Creates one active participation structure for a pre-existing actor. It is a distinct campaign transaction. |
| Profile, abilities, species, level, proficiencies, HP, Size, Speed | Existing component schemas and owners | verified for storage | The bootstrap uses only already registered schemas; this interface does not change their meanings. |
| Official origin/class/spell state | CH3/CH4/CH10 and ruleset Features 31–32 | blocked/planned | No source-complete Wizard/Bard implementation exists. A temporary record must not substitute for it. |
| Official atomic character creation | CH5 | blocked | CH5's governed root remains intentionally separate and must not consume provisional records as receipts. |
| MCP transport | Existing `commit` kinds `effects` and `campaign` | verified | `procedure.mcp.add-tool` says to avoid a new kind where an existing kind fits. |

## Dependency tree

```text
Temporary recordable character setup                                      [awaiting confirmation]
├─ existing atomic world-change effects                                   [verified]
├─ existing C15 campaign attachment                                      [verified]
├─ supported base actor components                                        [verified]
├─ provisional-record vocabulary and procedure                            [implemented]
│  └─ catalog component/schema + procedure + runbook update              [implemented]
└─ playtest verification                                                   [focused verified; full suite blocked]
   ├─ draft actor and record are atomic                                   [verified]
   ├─ valid C15 attachment changes no actor data                          [verified owner]
   ├─ active record revision remains schema-valid                         [ready]
   └─ unsupported rule labels produce no mechanics                        [ready]
```

## Proposed temporary vocabulary

| Role | Proposed ID | Meaning |
| --- | --- | --- |
| Record component | `dnd2024.playtest-character-record` | One actor-side, versioned provisional record. It holds only a lifecycle state and declared non-executable entries. It never grants a rule. |
| Governing procedure | `procedure.character.playtest-bootstrap` | Documents the three-step playtest flow, exact boundaries, revision semantics, and migration/retirement rule. It adds no MCP kind/tool. |

Proposed closed component shape:

```json
{
  "format": "dnd2024-playtest-character-record-v1",
  "state": "draft",
  "entries": [
    {
      "kind": "class",
      "key": "wizard",
      "label": "Wizard",
      "details": "GM/AI adjudicates unimplemented class behavior."
    }
  ]
}
```

`state` is exactly `draft`, `active`, or `retired`. Entries have a bounded closed `kind`
(`class`, `background`, `subclass`, `spell`, `equipment`, `feature`, `species-trait`, `feat`,
`rule-ruling`, or `note`), a stable local `key`, a display `label`, and optional plain-text
`details`. They are reference/narrative records only. They never contain a source-definition ID,
an effect, formula, DC, roll, resource balance, target, outcome, component JSON, item-instance
ID, campaign ID, actor ID, or copied rule text.

## Behavior and lifecycle

1. **Create draft.** One `effects` list creates the actor, all supported base components, and an
   add-only playtest record with `state: "draft"`. A failure creates none of them.
2. **Attach.** The caller uses C15's existing attach operation. It may attach only the actor
   created in step 1 and owns the participation entity/links.
3. **Activate record.** After a successful attachment, a `component.set` replaces the complete
   playtest record with identical entries and `state: "active"`.
4. **Revise.** While active, replace the complete record through `component.set`; operation
   history preserves the previous record. Revisions may add, retire, or correct entries but may
   not use an entry as authority to add an unimplemented rules component.
5. **Retire or migrate.** A future approved CH5/CH6 character is new authoritative state. It
   must not backfill official grants from this record. Mark the provisional record `retired` and
   link any human migration decision through ordinary campaign/history narration rather than
   inventing a mechanical migration.

The interface is purposefully a two-transaction setup because C15 only attaches a pre-existing
actor and no approved CH5 root exists. A stranded `draft` record is an honest, inspectable partial
playtest setup; it is never presented as a completed character.

## Failure and safety contract

- The catalog schema is the required record format and rejects malformed data in catalog/test
  validation. The existing trusted-host direct-effects route validates typed effect structure but
  does not enforce a component schema at runtime; a playtest caller must submit the closed form in
  this procedure. This is deliberately not presented as untrusted/player-facing validation.
- Existing actor ID/component: add-only creation fails unchanged.
- Missing/inactive campaign or C15 conflict: attachment fails unchanged; the draft actor/record
  remains available for correction or retirement.
- Record marked `active` without valid C15 attachment: the procedure documents it as invalid
  operator state; no query or mechanic treats it as official character authority.
- Unknown record `kind`, malformed key, excessive data, formula/effect/target fields,
  or an attempt to store a rule result is outside the catalog schema and must not be submitted by
  a procedure-compliant caller. No action/mechanic consumes it as rule authority.

## Ordered implementation leaf

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | Catalog record vocabulary | Confirmed component/procedure IDs and exact schema | The catalog imports with the new component and procedure, but no executable D&D behavior changes. |
| 2 | Runbook interface | Leaf 1 | A fresh MCP session can discover the procedure, dry-run one actor creation, attach through C15, activate/revise the record, and see the record query back. |
| 3 | Evidence | Leaves 1–2 | Focused tests prove atomic draft creation/schema rejection, C15 separation, revision, and that record entries do not create rule state. |

## Lowest ready leaf

The confirmed slice implemented the catalog record vocabulary, governing procedure, playtest
runbook update, and focused test coverage. It introduced no SRD source change, C# game rule, MCP
kind/tool, or CH3–CH6 change. Full-suite acceptance remains blocked by the separate Feature 11
initiative-event harness failures and existing Feature 20 movement failures.

## Confirmation gates

Before runtime artifacts are added, confirm all of the following:

1. Permanent IDs: `dnd2024.playtest-character-record` and
   `procedure.character.playtest-bootstrap`.
2. The component is explicitly provisional/non-executable and may only be created/revised through
   existing direct effects during the playtest.
3. The three-state lifecycle and two-transaction draft → C15 attach → active flow are acceptable.
4. The proposed closed entry vocabulary is sufficient; this is intentionally not an arbitrary JSON
   blob and not a rules engine.

## Planning receipt

- Runtime artifacts created: none.
- Existing public MCP kinds reused: `effects`, `campaign`.
- New MCP tools/kinds proposed: none.
