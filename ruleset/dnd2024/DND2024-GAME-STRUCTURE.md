# D&D 2024 game structure

Status: **Draft product structure; confirmed principles, candidate data models**

Date: 2026-08-27

## Purpose

Define the game that DantesRoleplay is trying to deliver before adding more isolated features.
Development should follow the structure in which D&D teaches and runs the game: establish the
participants and their responsibilities, establish the rhythm of play, then add the rules and
content needed to support that play.

This document describes product structure. It does not authorize a new permanent ID, component
schema, migration, public operation, or runtime behavior. The data-model names below are conceptual
until a later dependency tree and implementation slice confirm their owners and representations.

## Sources and authority

- Rules authority: `dnd2024.source.srd-5.2.1`, *Playing the Game > Rhythm of Play* (PDF p. 5).
  The SRD defines social interaction, exploration, and combat as the three main pillars and gives
  the repeating describe-decide-resolve rhythm.
- Role overview: [D&D Beyond Basic Rules — Playing the Game](https://www.dndbeyond.com/sources/dnd/br-2024/playing-the-game),
  especially *Player or DM?* and *Rhythm of Play*.
- DM guidance: [D&D Beyond Basic Rules — The Basics](https://www.dndbeyond.com/sources/dnd/br-2024/the-basics),
  especially *What Does a DM Do?* and *How to Run a Session*. This is product guidance; rules-bearing
  behavior still requires the repository's SRD source and exact locator.
- Existing narration owner: [procedure.play.storytelling](../../catalog/procedures/play/procedure.play.storytelling.md).
  It is a useful partial DM contract, not the complete game structure.
- Architecture boundary: [AGENTS.md](../../AGENTS.md) and [ARCHITECTURE.md](../../ARCHITECTURE.md).

## Confirmed product principles

1. **Chat is the game.** The main chat is always the primary interface through which players play
   D&D. A complete game must remain playable through chat without requiring the web UI.
2. **The web UI is a companion.** It may show character sheets, maps, combat state, inventory,
   notes, rules references, and other useful projections. It must not become a second game loop or
   a competing authority.
3. **The user is always a player while playing.** A user message is player participation: dialogue,
   a declared character intention, a question, planning, feedback, or another contribution to the
   shared game.
4. **The AI has an explicit role on every response.** It may act as the DM or as a player. When it
   acts as DM, it selects one primary DM responsibility appropriate to the current need. Its active
   role and responsibility must shape what context it receives and how it responds.
5. **The manuals drive development order.** We learn and implement the game in the order its rules
   explain how to play. Memory, convention, an old implementation, or a convenient UI design does
   not override the current D&D source.
6. **Conversation does not replace game authority.** AI prose may propose, explain, describe, or
   interpret. Rule mechanics determine uncertain outcomes, and accepted state transitions determine
   what durably happened.

## How to abstract the manual

For each section of the manual, extract the following parts before designing code:

| Part | Question |
| --- | --- |
| Participants | Who takes part? |
| Responsibilities | What is each participant expected to do? |
| Play loop | What causes the next contribution or decision? |
| Information | What may each participant know, perceive, or reveal? |
| Content | Which characters, places, objects, situations, and rules are involved? |
| Resolution | When is narration sufficient, and when must a rule resolve uncertainty? |
| State | What can change durably as a result? |
| Presentation | What belongs in chat, and what may the companion UI project? |

A noun in the manual is not automatically a database table. It might instead be:

- durable game state;
- authored catalog content;
- a versioned instruction;
- an ephemeral turn value;
- a derived view; or
- ordinary narration with no independent identity.

## Participants and roles

### Player

A player controls an adventurer, collaborates with the party, and decides what that character tries
to do. A player may also ask questions, plan with the party, describe character expression, and
help interpret a result in the fiction.

During normal play, the human user always occupies this product role. An AI may also occupy the
Player role when it controls an explicitly assigned adventurer. An AI Player receives only the
knowledge and state available to that character.

### Dungeon Master

The DM presents and runs the game but is not the opponent of the players. The manual describes the
DM through several responsibilities. These should become prepared AI instruction profiles, with
one primary responsibility selected for each AI response:

| DM responsibility | Purpose in the chat | Typical trigger |
| --- | --- | --- |
| Actor | Portray an NPC or monster from its knowledge, attitude, motives, and capabilities. | A player speaks to or observes a creature. |
| Director | Frame what is encountered, manage attention and pace, and move between meaningful moments. | A scene opens, closes, stalls, or changes focus. |
| Improviser | Respond coherently to an unprepared choice and derive plausible consequences. | Players attempt something the prepared material did not anticipate. |
| Referee | Apply or interpret the rules fairly when the outcome or procedure is unclear. | An action needs adjudication or rules conflict resolution. |
| Storyteller | Present situations, descriptions, consequences, and decision points as engaging fiction. | The game needs scene narration or an outcome translated into fiction. |
| Teacher | Explain how to play or clarify a rule at the player's current level of understanding. | A player asks how something works or appears confused. |
| Worldbuilder | Prepare or extend places, people, history, conflicts, and other setting material. | Preparation or durable setting creation is required. |

The responsibility is not a separate authority and does not give the AI permission to bypass the
rules or state owners. It is the behavioral lens for one response.

### Role separation

The following concepts must remain distinct:

- **Participant:** the human or AI taking part.
- **Game role:** Player or DM.
- **DM responsibility:** Actor, Director, Improviser, Referee, Storyteller, Teacher, or Worldbuilder.
- **Controlled actor:** the character, NPC, or monster whose choices the participant may make.
- **Speaker:** who is speaking in a particular piece of dialogue.
- **Turn owner:** who is currently expected or permitted to contribute.

The same underlying AI may be used for DM and Player responses, but those must be separate,
role-scoped invocations. A Player invocation must never receive hidden DM knowledge merely because
the same model handled an earlier DM response.

## Rhythm of play

The manual's three-step rhythm is the primary game loop:

1. **The DM describes the scene.** The players receive the information their characters can
   perceive and need in order to choose.
2. **The players describe what their characters do.** Their messages may contain dialogue,
   questions, planning, or one or more character intentions.
3. **The DM resolves and narrates what happens.** Trivial certainty can be narrated directly.
   Meaningful uncertainty goes through an applicable rule mechanic before its result is narrated.

The result normally creates a new situation or decision point, returning the game to step 1.

```text
DM frames a perceivable situation
              ↓
Player contributes dialogue, questions, or intention
              ↓
DM chooses the required responsibility
              ↓
Resolve directly or through an authoritative mechanic
              ↓
Apply accepted effects and record durable events
              ↓
DM narrates the visible result and next decision point
```

### Pillars and structure

Social interaction, exploration, and combat are modes of the same loop rather than separate games.

- **Social interaction** emphasizes dialogue, attitude, knowledge, motives, and influence.
- **Exploration** emphasizes locations, perception, movement, time, hazards, clues, and resources.
- **Combat** adds strict initiative order, rounds, turns, actions, movement, attacks, damage, and
  conditions.

Combat turns must not be confused with conversational turns. A single player message can contain
conversation plus one combat-turn declaration; a combat turn is authoritative rules state, while a
chat turn is interaction structure.

### Session structure

A typical session is a sequence of encounters or scenes, preceded by orientation or recap and
followed by closure and notes. The structures overlap rather than forming one strict containment
tree:

```text
World / setting
└── Campaign
    ├── Adventures and continuing story threads
    ├── Sessions, which record actual play over time
    └── Current play
        └── Scene or encounter
            └── Repeating describe-decide-resolve loop
```

A session may cross an adventure boundary. A scene may contain more than one pillar. An encounter
may be social, exploratory, combat-focused, or may move between them.

## AI response preparation

Before every AI response, the host should eventually be able to prepare a bounded turn context with:

- active game role;
- primary DM responsibility, when applicable;
- controlled character or creature, when applicable;
- campaign, session, scene, and encounter context;
- current pillar and any structured turn owner;
- role-authorized knowledge and perceptions;
- recent verified events and unresolved player intentions;
- applicable procedures, mechanics, and exact rule sources;
- campaign tone, expectations, and safety boundaries; and
- the allowed response/result shape.

The AI response may produce narration, dialogue, a rules explanation, a clarification question, a
player intention, a bounded proposal, or a typed inability to proceed. It may not promote its own
prose into a rule outcome or durable fact.

## Candidate conceptual data models

These are the models implied by the opening rules and DM guidance. They are candidates for later
owner and schema decisions, not approved implementation IDs.

### Participation and control

| Model | Meaning | Likely representation |
| --- | --- | --- |
| Participant | A human or AI taking part in the game. | Durable identity outside rules state. |
| Game-role assignment | A participant acting as Player or DM for a bounded context. | Durable session/campaign assignment plus per-turn selection. |
| AI role profile | Prepared instructions and constraints for DM or Player behavior. | Versioned D&D catalog instruction. |
| DM responsibility | The one primary DM function selected for an AI response. | Ephemeral turn classification referencing a versioned profile. |
| Character control | Which participant may choose actions for an adventurer. | Durable campaign/session relationship. |
| Creature control | Which participant or DM controls an NPC or monster in the current context. | Derived from game role and encounter state unless an exception is recorded. |
| Speaker | The character or narrator whose words a message represents. | Message metadata; not necessarily a world entity. |

### Chat and turn interaction

| Model | Meaning | Likely representation |
| --- | --- | --- |
| Play conversation | The primary chat through which the game is played. | Durable interaction container linked to campaign/session. |
| Message | One participant contribution. | Durable transcript evidence, not game-state authority by itself. |
| Interaction turn | One request/response opportunity in chat. | Ephemeral plus an auditable receipt. |
| Player contribution | Dialogue, question, planning, feedback, or character intention extracted without assuming success. | Structured interpretation of a message. |
| Character intention | What a character attempts or wants to do. | Pending proposal until resolved. |
| Clarification request | Missing information needed before an intention can be resolved. | Ephemeral turn state. |
| Decision point | A concrete situation awaiting player choice. | Derived scene state; persist only when needed for resume. |
| AI response | Narration, dialogue, teaching, ruling, question, or proposed player action. | Typed response plus visible chat text. |
| Turn handoff | Who should respond next and in what role. | Derived orchestration result. |

### Play structure

| Model | Meaning | Likely representation |
| --- | --- | --- |
| World or setting | The persistent fictional environment and its truths. | Durable live state plus authored setting content. |
| Campaign | A continuing series of adventures with a consistent group and narrative. | Durable root and source profile. |
| Adventure | A bounded collection of situations, encounters, characters, and goals. | Authored or runtime-created content with progress state. |
| Session | One period of actual play, including start, resume, recap, and end. | Durable operational record. |
| Scene | A continuous fictional situation with place, participants, and immediate context. | Durable only when required for continuation; otherwise derivable from events. |
| Encounter | A situation offering meaningful choices or challenges in one or more pillars. | Prepared content plus optional active state. |
| Pillar or play mode | Social interaction, exploration, or combat. | Derived classification, except where rules require explicit combat state. |
| Combat round and turn | Structured combat order and action opportunity. | Durable authoritative encounter state. |

### Fictional participants and world state

| Model | Meaning | Likely representation |
| --- | --- | --- |
| Character or adventurer | A protagonist controlled by a player. | Durable entity with D&D components. |
| Party | The adventurers who cooperate in the campaign. | Durable group/relationships. |
| NPC | A nonplayer person portrayed by the DM. | Durable entity when recurring or consequential. |
| Monster or creature | A creature the DM controls unless assigned otherwise. | Authored definition plus runtime instance state. |
| Location | A place characters can occupy, perceive, or explore. | Durable world entity and topology. |
| Relationship | A meaningful connection between people, groups, places, or things. | Durable directed relationship with bounded meaning. |
| Motive or goal | What a character, NPC, monster, or faction wants. | Durable private or visible state. |
| Attitude | Friendly, indifferent, hostile, or another source-defined social stance. | Contextual state, not a replacement for motive. |
| Knowledge item | A fact, rumour, secret, clue, or interpretation. | Durable information with provenance and visibility. |
| Perception or reveal | Information that becomes available to a particular character or group. | Durable acquisition/event when continuity requires it. |
| Item or treasure | A physical or rules-bearing object. | Authored definition plus durable runtime instance. |
| Condition or effect | A rules-defined state affecting a creature or object. | Durable component/effect with explicit owner. |
| World event | Something that happened and may affect later play. | Append-only durable event. |
| World time | The fictional time used for travel, rests, durations, and chronology. | Durable authoritative clock. |

### Story preparation and table agreement

| Model | Meaning | Likely representation |
| --- | --- | --- |
| Campaign premise | The broad situation, themes, and reason the characters adventure together. | Authored campaign material. |
| Adventure hook | A reason the party may engage with an adventure. | Authored or runtime-created story content. |
| Situation or challenge | A problem that invites player choice without prescribing the solution. | Encounter/scene content. |
| Story thread | An unresolved conflict, question, promise, or consequence spanning scenes. | Durable campaign/adventure state. |
| Prepared encounter | DM preparation for a likely social, exploration, or combat situation. | Authored content, not guaranteed future truth. |
| Recap | A bounded account of verified prior events. | Durable session artifact derived from evidence. |
| Game expectation | Agreed tone, themes, play style, and participation expectations. | Durable campaign agreement. |
| Safety boundary | Subjects or treatments the group has agreed to exclude or constrain. | Private, access-controlled campaign agreement. |
| House rule | An explicit departure from or addition to the core rules. | Separate versioned source selected by the campaign. |
| Rules interpretation | A recorded campaign ruling for a genuine ambiguity. | Durable ruling with source locator and scope. |

### Rule resolution

| Model | Meaning | Likely representation |
| --- | --- | --- |
| Rule source | The exact ruleset/version from which behavior is derived. | Registered immutable source identity. |
| Rule definition | General or specific rules content. | Catalog data, schema, procedure, or mechanic. |
| Rule exception | A more specific feature, spell, item, or ability that supersedes a general rule. | Declared catalog dependency/precedence, not an ad hoc C# branch. |
| Mechanic invocation | A request to resolve one supported uncertain action. | Ephemeral validated operation. |
| Roll or test | Random input and modifiers used by a rule. | Deterministic, auditable mechanic data. |
| Resolution | The accepted success, failure, cost, or other outcome. | Mechanic result linked to its exact source/version. |
| Effect | A proposed typed state change accepted by the kernel. | Transactional operation data. |
| Narrated result | The role-safe fictional presentation of a verified resolution. | Chat output; never the state authority itself. |

### Companion UI projections

| Model | Meaning | Authority |
| --- | --- | --- |
| Character-sheet view | Player-readable projection of character state. | Read-only projection of authoritative state. |
| Scene view | Visible location, participants, current situation, and decision point. | Read-only role-safe projection. |
| Combat tracker | Initiative, round, turn, participants, HP, and visible conditions. | Read-only projection plus authorized action submission. |
| Inventory view | Visible carried, equipped, or contained items. | Read-only projection plus authorized intent submission. |
| Map view | Known locations, topology, and tactical position when applicable. | Knowledge-filtered projection. |
| Rules reference | Source-cited explanation relevant to the current situation. | Catalog projection, never a browser-owned rule. |

## First product foundation

Before adding broad spell, monster, equipment, or class coverage, the product should prove this
small end-to-end structure:

1. A human user enters the primary chat as a Player.
2. The user has an assigned character in a campaign and session.
3. The AI answers as DM with an explicit primary responsibility.
4. The DM describes one role-safe scene and ends at a decision point.
5. The player declares an intention in ordinary language.
6. The DM determines whether narration is sufficient or an implemented mechanic is required.
7. Any mechanic resolves through the authoritative rule and state boundary.
8. The DM narrates the visible result without choosing the player's next action.
9. Durable changes can be resumed from state without relying on transcript memory.
10. The companion web UI reflects the same state without becoming required for play.

This is the acceptance spine for later manual-derived features. Each additional manual section
should extend a recognizable part of this loop and demonstrate it in play.

## Boundaries to preserve

- A DM responsibility is an instruction profile, not a new rules authority.
- A Player AI controls only its assigned character and receives only that character's permitted
  knowledge.
- Narration does not prove that an action succeeded or that state changed.
- A chat message is evidence of participation, not automatically a valid action command.
- Scene, encounter, session, adventure, campaign, and world are related but not interchangeable.
- Combat order is authoritative game state; conversational ordering is orchestration state.
- Prepared story material is possibility until introduced or established in play.
- Durable improvisation must pass through an explicit authored/live-state boundary.
- The web companion reads the same authority as chat and never owns independent game truth.
- D&D-specific responsibilities, terminology, and persona instructions belong in the D&D catalog
  or application adapter, never in generic C#.

## Development method from here

For each manual section:

1. Read the exact section and record its source locator.
2. Extract participants, responsibilities, loop, information, content, resolution, state, and
   presentation.
3. Map each concept to an existing owner before proposing a new model.
4. Classify every proposed model as catalog content, live state, instruction, ephemeral operation,
   or projection.
5. Define one playable acceptance example in the primary chat.
6. Implement the smallest coherent slice needed for that example.
7. Validate the companion UI only as a projection of the same accepted behavior.

The next structural design step is to confirm the participant/role model and the DM responsibility
selection contract. No runtime implementation should begin from this document alone.
