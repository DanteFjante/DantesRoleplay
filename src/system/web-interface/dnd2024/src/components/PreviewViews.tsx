import type {
  CurrentSceneAffordance,
  CurrentSituationReadModel,
  VisualMedia,
  WorldLocation,
  Perspective,
} from "../data/hub-types";
import { useEffect } from "react";
import { Icon } from "./Icon";
import { MediaImage } from "./MediaImage";
import { markCombatBoardReady } from "../observability/performance.js";
import { CombatBoard } from "./CombatBoard";

function ViewIntro({ eyebrow, title, copy }: { eyebrow: string; title: string; copy: string }) {
  return (
    <header className="view-intro">
      <span className="eyebrow">{eyebrow}</span>
      <h1 id="main-view-heading" tabIndex={-1}>{title}</h1>
      <p>{copy}</p>
    </header>
  );
}

function LocationContextPanel({
  headingId,
  location,
}: {
  headingId: string;
  location: WorldLocation;
}) {
  const observations = Array.isArray(location.observations) ? location.observations : [];
  return (
    <section className="current-scene-panel current-location-context" aria-labelledby={headingId}>
      <header><Icon name="Eye" size={18} /><h2 id={headingId}>Where you are</h2></header>
      <p>{location.description || "Location description is unavailable."}</p>
      {observations.length ? (
        <ul>{observations.map((observation) => <li key={observation}>{observation}</li>)}</ul>
      ) : <p className="current-scene-empty">No additional observations are available for this place.</p>}
    </section>
  );
}

function DmLocationContext({ location }: { location: WorldLocation }) {
  return location.dmSecret ? (
    <aside className="current-scene-dm" aria-label="Dungeon Master context">
      <span className="eyebrow">Behind the screen</span>
      <p>{location.dmSecret}</p>
    </aside>
  ) : null;
}

function SceneAffordancesPanel({ items, partial = false }: { items: CurrentSceneAffordance[]; partial?: boolean }) {
  if (items.length === 0 && !partial) return null;
  return (
    <section className="current-scene-panel current-affordances-panel" aria-labelledby="current-affordances-title">
      <header><Icon name="Compass" size={18} /><h2 id="current-affordances-title">Available now</h2></header>
      {items.length ? <ul className="current-affordances-list">
        {items.map((item) => (
          <li key={item.key}><strong>{item.label}</strong><span>{item.summary ?? "Affordance details unavailable."}</span></li>
        ))}
      </ul> : null}
      {partial ? <p className="current-scene-empty">Some scene affordances are unavailable for this read.</p> : null}
    </section>
  );
}

export function CurrentViewPreview({
  image,
  location,
  situation,
  perspective = "player",
  draftScope,
  onBoardAccepted,
}: {
  image: VisualMedia | null;
  location: WorldLocation | null;
  situation: CurrentSituationReadModel;
  perspective?: Perspective;
  draftScope?: Omit<import("../server/board-draft").BoardDraftScope, "encounterId">;
  onBoardAccepted?: () => void;
}) {
  const affordancesPartial = situation.unavailableFields?.includes("affordances") ?? false;
  const fieldPartial = (field: string) => situation.unavailableFields?.some((candidate) =>
    candidate === field || candidate.startsWith(`${field}.`)) ?? false;
  const locationDetailsPartial = fieldPartial("world.locations") || fieldPartial("campaign.mapOverlays");
  useEffect(() => {
    if (situation.status === "ready" && situation.kind === "combat" && situation.combat.board) {
      markCombatBoardReady(situation.combat.id);
    }
  }, [situation]);

  if (situation.status === "ready" && situation.kind === "recorded") {
    const recordedParticipants = Array.isArray(situation.recorded.participants)
      ? situation.recorded.participants : [];
    const recordedInteractions = Array.isArray(situation.recorded.interactions)
      ? situation.recorded.interactions : [];
    const participantsUnavailable = situation.unavailableFields?.includes("participants") ?? false;
    const interactionsUnavailable = situation.unavailableFields?.includes("interactions") ?? false;
    const label = situation.recorded.kind.split("-").map(value =>
      value.length ? value[0].toUpperCase() + value.slice(1) : value).join(" ");
    return (
      <div className="supporting-view current-scene-view current-scene-view--recorded">
        <ViewIntro
          copy="The latest durable play situation shared across AI clients and browser refreshes."
          eyebrow={label}
          title={situation.recorded.location?.name
            ? `${label} at ${situation.recorded.location.name}`
            : label}
        />
        <section className={`current-scene-card current-situation-focus${image ? "" : " current-scene-card--text-only"}`}>
          {image ? <div className="current-scene-card__visual has-image">
            <MediaImage fallback={<Icon name={situation.recorded.kind === "combat" ? "Swords" : "UsersRound"} size={30} />} loading="eager" media={image} />
          </div> : null}
          <div className="current-scene-card__copy">
            <span className="eyebrow">{situation.recorded.location?.name ?? location?.name ?? "Current play session"}</span>
            <h2>{label}</h2>
            <p>{situation.recorded.summary ?? "Recorded situation summary unavailable."}</p>
          </div>
        </section>
        <div className="current-scene-grid">
          <section className="current-scene-panel" aria-labelledby="current-recorded-participants">
            <header><Icon name="UsersRound" size={18} /><h2 id="current-recorded-participants">Participants</h2></header>
            {recordedParticipants.length ? (
              <div className="current-scene-people">
                {recordedParticipants.map((participant) => (
                  <article key={participant.id}>
                    <span aria-hidden="true">{participant.name.slice(0, 2).toUpperCase()}</span>
                    <div><strong>{participant.name}</strong><small>{label} participant</small></div>
                  </article>
                ))}
              </div>
            ) : <p className="current-scene-empty">{participantsUnavailable
              ? "Participant identities are unavailable for this read."
              : "No participant identities were recorded for this situation."}</p>}
            {recordedParticipants.length && fieldPartial("participants")
              ? <p className="current-scene-empty">Some participant identities are unavailable for this read.</p> : null}
          </section>
          {location ? <LocationContextPanel headingId="current-recorded-location" location={location} /> : null}
          <section className="current-scene-panel" aria-labelledby="current-recorded-interactions">
            <header><Icon name="Clock3" size={18} /><h2 id="current-recorded-interactions">Recent interactions</h2></header>
            {recordedInteractions.length ? (
              <ol className="current-recorded-interactions">
                {recordedInteractions.map((message) => (
                  <li key={message.id}>
                    <strong>{message.role === "player" ? "You" : "Game AI"}</strong>
                    <p className="message">{message.text}</p>
                  </li>
                ))}
              </ol>
            ) : <p className="current-scene-empty">{interactionsUnavailable
              ? "Recorded dialogue is unavailable for this read."
              : "No recorded dialogue is available yet."}</p>}
            {recordedInteractions.length && fieldPartial("interactions")
              ? <p className="current-scene-empty">Some recorded dialogue is unavailable for this read.</p> : null}
          </section>
        </div>
      </div>
    );
  }

  if (situation.status === "unavailable" || (situation.status === "ready" && situation.kind === "exploration" && !location)) {
    return (
      <div className="supporting-view current-scene-view">
        <ViewIntro
          copy="The table's immediate context, without guessing from campaign prose or map selection."
          eyebrow="Current situation"
          title="No current scene"
        />
        <section className="current-scene-unavailable" aria-labelledby="current-scene-unavailable-title">
          <span><Icon name="Compass" size={28} /></span>
          <div>
            <h2 id="current-scene-unavailable-title">Current view unavailable</h2>
            <p>{situation.status === "unavailable"
              ? situation.message
              : "The game server has not projected an exact current location for this seat."}</p>
          </div>
        </section>
        {situation.status === "unavailable" && location
          ? <LocationContextPanel headingId="current-unavailable-location" location={location} />
          : null}
      </div>
    );
  }

  if (situation.status === "ready" && situation.kind === "conversation") {
    const locationName = location?.name ?? "Current location unavailable";
    return (
      <div className="supporting-view current-scene-view current-scene-view--conversation">
        <ViewIntro
          copy={situation.conversation.summary ?? "A conversation is currently in progress."}
          eyebrow={locationName}
          title={situation.conversation.name}
        />
        <section className={`current-scene-card current-situation-focus${image ? "" : " current-scene-card--text-only"}`}>
          {image ? <div className="current-scene-card__visual has-image">
            <MediaImage fallback={<Icon name="UsersRound" size={30} />} loading="eager" media={image} />
          </div> : null}
          <div className="current-scene-card__copy">
            <span className="eyebrow">{locationName}</span>
            <h2>{situation.conversation.name}</h2>
            <p>{situation.conversation.summary ?? "A conversation is currently in progress."}</p>
          </div>
        </section>
        <div className="current-scene-grid current-conversation-grid">
          {location ? <LocationContextPanel headingId="current-conversation-location" location={location} /> : null}
          <section className="current-scene-panel" aria-labelledby="current-conversation-people">
            <header><Icon name="UsersRound" size={18} /><h2 id="current-conversation-people">Visible participants</h2></header>
            {situation.conversation.participants.length ? (
              <div className="current-scene-people">
                {situation.conversation.participants.map((participant) => (
                  <article key={participant.id}>
                    <span><MediaImage fallback={<span aria-hidden="true">{participant.name.slice(0, 2).toUpperCase()}</span>} media={participant.portrait} /></span>
                    <div><strong>{participant.name}</strong><small>Conversation participant</small></div>
                  </article>
                ))}
              </div>
            ) : <p className="current-scene-empty">No participant identities are available to this view.</p>}
            {situation.conversation.participants.length && fieldPartial("conversation.participants")
              ? <p className="current-scene-empty">Some participant identities are unavailable for this read.</p> : null}
          </section>
        </div>
        <SceneAffordancesPanel items={situation.affordances ?? []} partial={affordancesPartial} />
        {location ? <DmLocationContext location={location} /> : null}
      </div>
    );
  }

  if (situation.status === "ready" && situation.kind === "combat") {
    const { combat } = situation;
    const locationName = location?.name ?? "Current location unavailable";
    return (
      <div className="supporting-view current-scene-view current-scene-view--combat">
        <ViewIntro
          copy={combat.turn ? `${combat.turn.actorName} has the active turn.` : fieldPartial("combat.turn")
            ? "The active turn is unavailable for this read." : "Initiative and encounter state are ready."}
          eyebrow={locationName}
          title={combat.name}
        />
        <section className={`current-scene-card current-situation-focus${image ? "" : " current-scene-card--text-only"}`}>
          {image ? <div className="current-scene-card__visual has-image">
            <MediaImage fallback={<Icon name="Swords" size={30} />} loading="eager" media={image} />
          </div> : null}
          <div className="current-scene-card__copy">
            <span className="eyebrow">{locationName}</span>
            <h2>{combat.name}</h2>
            <div className="current-scene-card__facts" aria-label="Encounter facts">
              <span><Icon name="Clock3" size={15} /> {combat.round ? `Round ${combat.round.number}`
                : fieldPartial("combat.round") ? "Round unavailable" : "Turns not started"}</span>
              <span><Icon name="UsersRound" size={15} /> {combat.participants.length} visible combatants</span>
              <span><Icon name="Swords" size={15} /> {combat.turn ? `${combat.turn.actorName}'s turn`
                : fieldPartial("combat.turn") ? "Active turn unavailable" : "No active turn"}</span>
            </div>
          </div>
        </section>
        <CombatBoard key={combat.id} board={combat.board} perspective={perspective} background={combat.background}
          draftScope={draftScope ? { ...draftScope, encounterId: combat.id } : undefined} onAccepted={onBoardAccepted} />
        <div className="current-scene-grid current-combat-grid">
          <section className="current-scene-panel" aria-labelledby="current-initiative-title">
            <header><Icon name="Swords" size={18} /><h2 id="current-initiative-title">Initiative</h2></header>
            {combat.participants.length ? (
              <ol className="current-initiative-list">
                {combat.participants.map((participant) => (
                  <li className={participant.active ? "is-active" : ""} key={participant.id}>
                    <span>{participant.initiative}</span><strong>{participant.name}</strong>
                    {participant.active ? <small>Current turn</small> : null}
                  </li>
                ))}
              </ol>
            ) : <p className="current-scene-empty">No combatant identities are available to this view.</p>}
            {combat.participants.length && fieldPartial("combat.participants")
              ? <p className="current-scene-empty">Some combatant identities are unavailable for this read.</p> : null}
          </section>
          <section className="current-scene-panel" aria-labelledby="current-turn-title">
            <header><Icon name="Clock3" size={18} /><h2 id="current-turn-title">Active turn</h2></header>
            {combat.turn ? (
              <div className="current-turn-summary">
                <strong>{combat.turn.actorName}</strong>
                {combat.turn.budget ? (
                  <dl>
                    <div><dt>Actions</dt><dd>{combat.turn.budget.actions}</dd></div>
                    <div><dt>Bonus actions</dt><dd>{combat.turn.budget.bonusActions}</dd></div>
                    <div><dt>Reactions</dt><dd>{combat.turn.budget.reactions}</dd></div>
                  </dl>
                ) : <p>Turn resources are not available to this view.</p>}
              </div>
            ) : <p className="current-scene-empty">{fieldPartial("combat.turn")
              ? "The active turn is unavailable for this read." : "This encounter has no active turn."}</p>}
          </section>
          {location ? <LocationContextPanel headingId="current-combat-location" location={location} /> : null}
        </div>
        <SceneAffordancesPanel items={situation.affordances ?? []} partial={affordancesPartial} />
        {location ? <DmLocationContext location={location} /> : null}
      </div>
    );
  }

  if (!location) return null;
  return (
    <div className="supporting-view current-scene-view">
      {(() => {
        const people = Array.isArray(location.people) ? location.people : [];
        const routes = Array.isArray(location.routes) ? location.routes : [];
        const observations = Array.isArray(location.observations) ? location.observations : [];
        return <>
      <section className={`current-scene-card${image ? "" : " current-scene-card--text-only"}`}>
        {image ? <div className="current-scene-card__visual has-image">
          <MediaImage fallback={<Icon name="Compass" size={30} />} loading="eager" media={image} />
        </div> : null}
        <div className="current-scene-card__copy">
          <span className="eyebrow">{location.region === location.name ? "Exploration" : `${location.region} · Exploration`}</span>
          <h1 id="main-view-heading" tabIndex={-1}>{location.name}</h1>
          <p>{location.description || "Location description is unavailable."}</p>
          <div className="current-scene-card__facts" aria-label="Scene facts">
            <span><Icon name="MapPin" size={15} /> {location.kind} · {location.status}</span>
            <span><Icon name="UsersRound" size={15} /> {people.length} {people.length === 1 ? "person" : "people"} shown</span>
            <span><Icon name="Route" size={15} /> {routes.length} {routes.length === 1 ? "way" : "ways"} shown</span>
          </div>
        </div>
      </section>
      {locationDetailsPartial ? <p className="current-scene-empty">Some location details are unavailable for this read.</p> : null}
      <div className="current-scene-grid">
        <section className="current-scene-panel" aria-labelledby="current-observations-title">
          <header><Icon name="Eye" size={18} /><h2 id="current-observations-title">What you notice</h2></header>
          {observations.length ? (
            <ul>{observations.map((observation) => <li key={observation}>{observation}</li>)}</ul>
          ) : <p className="current-scene-empty">No observations have been projected for this place.</p>}
        </section>
        <section className="current-scene-panel" aria-labelledby="current-people-title">
          <header><Icon name="UsersRound" size={18} /><h2 id="current-people-title">People here</h2></header>
          {people.length ? (
            <div className="current-scene-people">
              {people.map((person) => (
                <article key={person.id}>
                  <span><MediaImage fallback={<span aria-hidden="true">{person.initials}</span>} media={person.portrait} /></span>
                  <div><strong>{person.name}</strong><small>{person.role}</small></div>
                </article>
              ))}
            </div>
          ) : <p className="current-scene-empty">No co-present people are visible in this projection.</p>}
        </section>
        <section className="current-scene-panel" aria-labelledby="current-routes-title">
          <header><Icon name="Route" size={18} /><h2 id="current-routes-title">Known ways onward</h2></header>
          {routes.length ? (
            <ul>{routes.map((route) => <li key={`${route.destination}-${route.detail}`}><strong>{route.destination}</strong><span>{route.detail}</span></li>)}</ul>
          ) : null}
          {situation.routesCoverage === "unavailable" ? (
            <p className="current-scene-empty">Known ways onward are unavailable for this read.</p>
          ) : situation.routesCoverage === "partial" ? (
            <p className="current-scene-empty">Known ways onward are partially available.</p>
          ) : !routes.length ? <p className="current-scene-empty">No known exits have been projected for this place.</p> : null}
        </section>
      </div>
      <SceneAffordancesPanel items={situation.affordances ?? []} partial={affordancesPartial} />
      <DmLocationContext location={location} />
        </>;
      })()}
    </div>
  );
}
