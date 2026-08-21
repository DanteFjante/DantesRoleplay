# Character Feature 1 Slice 1 receipt — immutable approved content

Implemented `dnd2024.character.content-definition` and its administrative recorder under
`procedure.mechanic.dnd2024.character-content-definition`. The closed component identifies one
versioned SRD source declaration only: kind, canonical key, version, active/archived status, and
source reference. It stores no grants, rules prose, character state, campaign scope, items, or
derived values.

The ratified CH0 fixture now has three immutable declarations:

- `content.dnd2024.species.human.v1`
- `content.dnd2024.background.soldier.v1`
- `content.dnd2024.class.fighter.v1`

## Verification

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterFeature01" -v minimal`
  passed **3 tests**. The build retried while another local test host briefly held its output DLL,
  then completed successfully.
- `roleplay validate catalog` validated **247 records** with **0 warnings**.
- `CharacterFeature01Slice1Tests` verifies fixture source identity, write-once recording,
  duplicate/invalid-source/invalid-key rollback, and schema refusal of copied grants or invalid
  versioned identities.

No character actor, campaign attachment, player profile, grant, item instance, or live catalog
import was created.
