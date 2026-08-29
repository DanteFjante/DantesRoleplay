# D&D 2024 web UI Slice 7D1 receipt — fixed local player seat

Status: **accepted 2026-08-27**
Implementation contract: [Slice 7D1 implementation](DND2024-WEB-UI-SLICE-7D1-IMPLEMENTATION.md)
Dependency leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7D1 / F2
Ruleset alignment: **ruleset-neutral host policy with catalog-owned D&D vocabulary**

## Delivered boundary

The current private host now registers the accepted authorized-knowledge core behind an explicit
fixed local player seat. The seat grants only `Actor` for the exact configured Orban/campaign pair,
only when the server-side peer is loopback. The browser cannot nominate or elevate its principal,
role, application, campaign, actor, state space, or visibility.

Application vocabulary is no longer embedded in C#. The D&D source owns one closed
`system.knowledge.binding.v1` document. A generic active-document reader accepts that document only
from the exact application activation winner after rechecking retained source registration, allowed
root containment, byte length, and SHA-256 fingerprint. The resolver then requires exactly one
registered application state space containing the exact active campaign root.

The generic participation verifier independently requires one active participation entity with
exactly one campaign owner and exactly one actor link. Missing, withdrawn, malformed, duplicate,
cross-campaign, or multi-actor structures deny.

## Delivered artifacts

| Area | Evidence |
| --- | --- |
| Activated source read | `IActivatedApplicationDocumentReader`, `ActivatedApplicationDocumentReader`, registration, and focused file-drift test under `src/system/application-activation/`. |
| Generic knowledge binding | Participation vocabulary and binding revision added to `KnowledgeApplicationBinding`; activated binding resolver and strict closed document parser added under `src/system/knowledge/`. |
| Participation proof | `ApplicationKnowledgeActorParticipationVerifier` added under the generic knowledge owner. |
| Local audience | `DantesRoleplay.MCPServer/LocalKnowledgeAudience.cs` adds current-configuration, loopback-only, actor-only authorization. |
| Current host composition | `ServerConfiguration` supplies explicit audience, binding, participation, candidate, and answer services without adding a route or MCP tool. |
| D&D vocabulary | `catalog/applications/dnd2024/metadata/authorized-knowledge.json` owns exact qualified types, relationships, fields, values, states, and neutral presentation mappings. |
| Host configuration | Development settings now identify `dnd2024`, `campaign.thalorien.brackenford`, and `actor.thalorien.brackenford.orban`; stale GM/short-campaign settings were removed. |
| Tests | Generic binding/participation/active-document tests, private-host locality/revocation/registration tests, and a D&D metadata parser test. |

No table, migration, component type, mechanic, procedure, query kind, entity fixture, campaign
relationship, event, notification, HTTP route, MCP tool, or browser component was added.

## Authorization and bypass evidence

- The existing policy-first candidate tests still prove denial and wrong-campaign grants touch no
  binding, ECS, edge, lexical, or completion dependency.
- Loopback with the exact configured campaign grants only the fixed actor. A remote peer, missing
  HTTP context, wrong campaign, disabled seat, malformed seat, or changed application denies.
- The configuration provider is read on every policy call; disabling the seat revokes the very next
  request and changes cannot reuse a cached grant.
- The activated binding has no request-selected application or state-space input. Missing, drifted,
  invalid, or ambiguous application metadata/campaign state returns no binding.
- Participation checks both edge direction/data and unique campaign/actor ownership. An extra actor
  edge invalidates the participation instead of widening it.
- `rg -n "dnd2024|game[.]core|thalorien|orban" src/system/knowledge -g "*.cs"` returned no matches.
  D&D and campaign identities occur only in the application metadata, host configuration, and the
  D&D-specific acceptance test.

## Verification evidence

| Gate | Result |
| --- | --- |
| Isolated build | Passed, 0 warnings / 0 errors. |
| Focused active-document, binding, participation, local-audience, and D&D metadata tests | **17 passed, 0 failed**. |
| Disposable catalog validation | **144 records valid**, 21 pre-existing/non-blocking near-duplicate warnings; tool confirmed no live data was touched. |
| Full shared suite after final hardening | **1,402 passed, 0 failed, 0 skipped**. |
| Protocol-walk build | Passed, 0 warnings / 0 errors. |
| Real MCP protocol walk | **6 passed, 0 failed, 2 intentionally skipped**; the public surface remains exactly `orient`, `query`, and `commit`. |
| JSON and whitespace checks | Development configuration files parsed; `git diff --check` passed (line-ending notices only). |

Tests used isolated build output because the development server owns the normal output files. All
stateful tests used disposable databases. No test or validation command wrote the live campaign.

## Deliberate exclusions and live boundary

- No actor or baseline knowledge state was authored, imported, inferred, or repaired. The live D&D
  state still has zero accepted knowledge-state/baseline links, so no player notebook is exposed.
- The newly authored metadata is not yet a winner in live D&D application activation revision 2.
  A read-only check found no `authorized-knowledge` activation document. Reactivating the source is
  an explicit live synchronization boundary and is intentionally left for the reviewed 7D2 work.
- The currently running development process predates this build. Restarting it alone would load the
  new host code/configuration, but the binding still fails closed until that explicit activation.
- No GM grant, remote/Tailscale access, public authentication, knowledge answer route/tool, browser
  notebook, map, or image surface was added.

## Exit

Order 7D1 is accepted. The lowest ready leaf is 7D2: review the actual facts Orban should know,
activate the reviewed D&D source metadata at the same explicit live synchronization boundary, and
record only approved baseline/actor knowledge state. Visibility, entity presence, filenames, and
prose remain non-authoritative and may not be used to infer knowledge.
