    const DND2024_MAXIMUM_PAGES = 10;
    const DND2024_MAXIMUM_RECORDS = 1000;
    const DND2024_CHARACTER_FILTER_CONCURRENCY = 6;
    const DND2024_INVENTORY_MAXIMUM_DEPTH = 4;
    const DND2024_INVENTORY_MAXIMUM_ENTRIES = 96;
    const DND2024_INVENTORY_PAGE_SIZE = 24;
    const DND2024_SCENE_MAXIMUM_ENTRIES = 24;
    const DND2024_DICE_SIDES = Object.freeze([4, 6, 8, 10, 12, 20]);
    const DND2024_ABILITIES = Object.freeze([
      ['str', 'Strength'], ['dex', 'Dexterity'], ['con', 'Constitution'],
      ['int', 'Intelligence'], ['wis', 'Wisdom'], ['cha', 'Charisma']
    ]);
    const DND2024_COMPONENTS = Object.freeze({
      abilities: 'dnd2024.abilities',
      armorClass: 'dnd2024.armor-class',
      characterExperience: 'dnd2024.character.experience',
      characterProfile: 'dnd2024.character.identity',
      conditions: 'dnd2024.conditions',
      creatureSize: 'dnd2024.creature-size',
      damageMitigation: 'dnd2024.damage-mitigation',
      encounterOrder: 'dnd2024.encounter-initiative-order',
      encounterTurn: 'dnd2024.encounter-turn-state',
      hitPoints: 'dnd2024.creature.hit-points',
      itemDefinition: 'dnd2024.item-definition',
      itemActivity: 'dnd2024.item-activity',
      itemInstance: 'dnd2024.item-instance',
      itemQuantity: 'dnd2024.item-quantity',
      level: 'dnd2024.character-level',
      languages: 'dnd2024.language-proficiencies',
      savingThrows: 'dnd2024.saving-throw-proficiencies',
      skills: 'dnd2024.skill-proficiencies',
      speed: 'dnd2024.speed',
      temporaryHitPoints: 'dnd2024.creature.temporary-hit-points',
      tools: 'dnd2024.tool-proficiencies',
      turnBudget: 'dnd2024.turn-budget',
      weapons: 'dnd2024.weapon-proficiencies',
      equipmentState: 'dnd2024.equipment-state'
    });
    const DND2024_CHARACTER_COMPONENTS = Object.freeze([
      DND2024_COMPONENTS.abilities,
      DND2024_COMPONENTS.level
    ]);
    const DND2024_CAMPAIGN_ROOT_COMPONENT = 'dnd2024.game.core.campaign.root';
    const DND2024_PARTICIPATION_COMPONENT = 'dnd2024.game.core.campaign.character-participation';
    const DND2024_LEGACY_CHARACTER_COMPONENT = 'dnd2024.playtest-character-record';
    const DND2024_WORLD_LOCATION_COMPONENT = 'dnd2024.game.core.world.location';
    const DND2024_WORLD_MOTIVE_COMPONENT = 'dnd2024.game.core.world.motive';
    const DND2024_CAMPAIGN_PARTICIPATION_KINDS = Object.freeze([
      'dnd2024.campaign.has-character-participation',
      'dnd2024.game.core.campaign.has-character-participation'
    ]);
    const DND2024_PARTICIPATION_ACTOR_KINDS = Object.freeze([
      'dnd2024.campaign.character-participation.for-actor',
      'dnd2024.game.core.campaign.character-participation.for-actor'
    ]);

    class Dnd2024CharacterSheet extends HTMLElement {
      connectedCallback() {
        if (!this.hasAttribute('role')) this.setAttribute('role', 'region');
        if (!this.hasAttribute('aria-label') && !this.hasAttribute('aria-labelledby')) {
          this.setAttribute('aria-label', 'Character sheet');
        }
      }
    }

    class Dnd2024Workspace extends HTMLElement {
      static get observedAttributes() {
        return ['application-id', 'state-space-id', 'campaign-id', 'entity-id', 'encounter-id'];
      }

      constructor() {
        super();
        this._connected = false;
        this._request = null;
        this._stateSpaces = [];
        this._stateSpaceEntities = [];
        this._entityComponentSummaries = new Map();
        this._campaigns = [];
        this._campaign = null;
        this._actionsAvailable = false;
        this._entities = [];
        this._encounters = [];
        this._encounterEntity = null;
        this._encounterComponents = new Map();
        this._encounterStatus = 'missing';
        this._components = new Map();
        this._entity = null;
        this._inventory = {status: 'missing', contents: [], boundary: null};
        this._currentContext = {status: 'missing', location: null, people: [], boundary: null};
        this._knowledge = {status: 'missing', entries: []};
        this._knowledgeQuery = '';
        this._knowledgeKind = 'all';
        this._selectedScenePersonId = '';
        this._healingAmount = 1;
        this._temporaryHitPointsAmount = 1;
        this._temporaryHitPointsChoice = 'keep';
        this._vitalActionGeneration = 0;
        this._equipmentModeByItem = new Map();
        this._inventoryTransferByItem = new Map();
        this._inventoryStackByItem = new Map();
        this._inventoryActivityByItem = new Map();
        this._inventoryActionGeneration = 0;
        this._selectedView = 'character';
        this._viewButtons = new Map();
        this._viewPanels = new Map();
        this.attachShadow({mode: 'open'});
        this._renderShell();
      }

      connectedCallback() {
        if (this._connected) return;
        this._connected = true;
        this._load();
      }

      disconnectedCallback() {
        this._connected = false;
        if (this._request) this._request.abort();
        this._request = null;
      }

      attributeChangedCallback(name, oldValue, newValue) {
        if (!this._connected || oldValue === newValue) return;
        if (name === 'application-id') this._load();
        if (name === 'state-space-id') this._loadCampaigns();
        if (name === 'campaign-id') this._loadCampaign();
        if (name === 'entity-id') this._loadEntity();
        if (name === 'encounter-id') this._loadEntity();
      }

      get applicationId() { return this.getAttribute('application-id')?.trim() || 'dnd2024'; }
      get selectedStateSpaceId() {
        const requested = this.getAttribute('state-space-id')?.trim();
        return requested && this._stateSpaces.some(item => item.stateSpaceId === requested)
          ? requested : this._stateSpaceSelect?.value || '';
      }
      get selectedEntityId() {
        const requested = this.getAttribute('entity-id')?.trim();
        return requested && this._entities.some(item => item.entityId === requested)
          ? requested : this._entitySelect?.value || '';
      }
      get selectedCampaignId() {
        const requested = this.getAttribute('campaign-id')?.trim();
        return requested && this._campaigns.some(item => item.entityId === requested)
          ? requested : this._campaignSelect?.value || '';
      }
      get selectedEncounterId() {
        const requested = this.getAttribute('encounter-id')?.trim();
        return requested && this._encounters.some(item => item.entityId === requested)
          ? requested : this._encounterSelect?.value || '';
      }

      _renderShell() {
        const style = document.createElement('style');
        style.textContent = `
          :host {
            --dnd-ink: #f6eedb;
            --dnd-muted: #b9ad96;
            --dnd-panel: rgba(24, 24, 27, .92);
            --dnd-panel-2: rgba(40, 36, 34, .9);
            --dnd-line: rgba(218, 192, 147, .24);
            --dnd-gold: #e0b968;
            --dnd-red: #a83b36;
            --dnd-green: #5f8d6b;
            display: block;
            color: var(--dnd-ink);
            font-family: Inter, ui-sans-serif, system-ui, sans-serif;
            min-width: 0;
          }
          * { box-sizing: border-box; }
          button, select { font: inherit; }
          button:focus-visible, select:focus-visible { outline: 3px solid #f3d797; outline-offset: 2px; }
          .shell { display: grid; gap: 1rem; min-width: 0; }
          .banner {
            align-items: center;
            background:
              radial-gradient(circle at 12% 10%, rgba(224,185,104,.18), transparent 28%),
              linear-gradient(125deg, rgba(54,24,23,.98), rgba(23,22,25,.98) 58%, rgba(30,37,32,.98));
            border: 1px solid var(--dnd-line);
            border-radius: 1rem;
            box-shadow: 0 1rem 2.5rem rgba(0,0,0,.25), inset 0 1px rgba(255,255,255,.04);
            display: grid;
            gap: 1rem;
            grid-template-columns: auto minmax(0, 1fr) auto;
            min-height: 8.5rem;
            overflow: hidden;
            padding: 1.15rem;
            position: relative;
          }
          .banner::after { background: linear-gradient(90deg, var(--dnd-gold), transparent); bottom: 0; content: ''; height: 2px; left: 0; position: absolute; right: 0; }
          .crest { align-items: center; background: #211716; border: 1px solid #8e6b3e; border-radius: 50%; color: var(--dnd-gold); display: flex; font-family: Georgia, serif; font-size: 2rem; font-weight: 800; height: 5.4rem; justify-content: center; transform: rotate(-4deg); width: 5.4rem; }
          .identity { min-width: 0; }
          .eyebrow { color: var(--dnd-gold); font-size: .68rem; font-weight: 800; letter-spacing: .18em; margin: 0 0 .25rem; text-transform: uppercase; }
          h2 { font-family: Georgia, 'Times New Roman', serif; font-size: clamp(1.7rem, 4vw, 3rem); letter-spacing: -.025em; line-height: .98; margin: 0; overflow-wrap: anywhere; }
          .scope { color: var(--dnd-muted); font-family: ui-monospace, monospace; font-size: .72rem; margin: .55rem 0 0; overflow-wrap: anywhere; }
          .level { align-self: start; background: rgba(224,185,104,.1); border: 1px solid #806a42; border-radius: 999px; color: #f4dca3; font-size: .75rem; font-weight: 800; padding: .48rem .7rem; text-transform: uppercase; }
          .toolbar { align-items: end; background: rgba(12,12,14,.72); border: 1px solid var(--dnd-line); border-radius: .85rem; display: grid; gap: .65rem; grid-template-columns: minmax(11rem, 1.3fr) repeat(3, minmax(9rem, 1fr)) auto; padding: .8rem; }
          label { color: var(--dnd-muted); display: grid; font-size: .68rem; font-weight: 800; gap: .3rem; letter-spacing: .08em; text-transform: uppercase; }
          select { background: #171719; border: 1px solid #635745; border-radius: .55rem; color: var(--dnd-ink); min-height: 2.65rem; min-width: 0; padding: .45rem .7rem; width: 100%; }
          .refresh { background: linear-gradient(#b44d43, #852d2a); border: 1px solid #d07162; border-radius: .55rem; color: white; cursor: pointer; font-weight: 800; min-height: 2.65rem; padding: .45rem 1rem; }
          .refresh:hover { filter: brightness(1.12); }
          .status { color: var(--dnd-muted); font-size: .78rem; margin: 0; min-height: 1.2em; }
          .status[data-error='true'] { color: #ffaaa2; }
          .view-navigation { background: linear-gradient(145deg, rgba(49,38,34,.92), rgba(20,20,23,.96)); border: 1px solid #6f5a3f; border-radius: .95rem; box-shadow: 0 .75rem 1.75rem rgba(0,0,0,.16); padding: .45rem; }
          .view-tabs { display: grid; gap: .45rem; grid-template-columns: repeat(5, minmax(0, 1fr)); }
          .view-tab { align-items: center; background: linear-gradient(160deg, #302d31, #201e21); border: 1px solid #625849; border-radius: .72rem; color: var(--dnd-ink); cursor: pointer; display: flex; gap: .62rem; justify-content: center; min-height: 3.65rem; padding: .58rem .75rem; position: relative; }
          .view-tab::after { border: 1px solid transparent; border-radius: .5rem; content: ''; inset: .25rem; pointer-events: none; position: absolute; }
          .view-tab:hover { filter: brightness(1.12); }
          .view-tab[aria-selected='true'] { background: radial-gradient(circle at 18% 12%, rgba(224,185,104,.19), transparent 34%), linear-gradient(145deg, #84342f, #4f2525); border-color: #d2aa60; box-shadow: inset 0 -3px #e0b968; color: #fff8e8; }
          .view-tab[aria-selected='true']::after { border-color: rgba(255,238,199,.18); }
          .view-tab-icon { align-items: center; border: 1px solid #8e744b; border-radius: 50%; color: var(--dnd-gold); display: flex; flex: 0 0 auto; font-family: Georgia, serif; font-size: .9rem; font-weight: 900; height: 2rem; justify-content: center; width: 2rem; }
          .view-tab-label { font-family: Georgia, serif; font-size: .86rem; font-weight: 800; letter-spacing: .08em; text-transform: uppercase; }
          .view-panel { min-width: 0; }
          .single-view { display: grid; }
          .combat-grid { display: grid; gap: 1rem; grid-template-columns: minmax(18rem, 1fr) minmax(18rem, 1fr); }
          .scene-grid { display: grid; gap: 1rem; grid-template-columns: minmax(18rem, .9fr) minmax(20rem, 1.1fr); }
          .dashboard { display: grid; gap: 1rem; grid-template-columns: minmax(0, 1.65fr) minmax(16rem, .85fr); }
          .main, .side { display: grid; gap: 1rem; min-width: 0; }
          .panel { background: var(--dnd-panel); border: 1px solid var(--dnd-line); border-radius: .9rem; box-shadow: 0 .75rem 1.75rem rgba(0,0,0,.16); min-width: 0; overflow: hidden; padding: 1rem; }
          .panel-heading { align-items: baseline; display: flex; gap: .65rem; justify-content: space-between; margin-bottom: .85rem; }
          h3 { color: #f4dca3; font-family: Georgia, serif; font-size: 1rem; letter-spacing: .08em; margin: 0; text-transform: uppercase; }
          .panel-note { color: var(--dnd-muted); font-size: .7rem; }
          .vitals { display: grid; gap: .65rem; grid-template-columns: minmax(9rem, 1.5fr) repeat(3, minmax(6rem, 1fr)); }
          .vital { align-items: center; background: var(--dnd-panel-2); border: 1px solid var(--dnd-line); border-radius: .78rem; display: flex; gap: .7rem; min-height: 6.4rem; padding: .8rem; }
          .vital strong { display: block; font-family: Georgia, serif; font-size: 1.7rem; line-height: 1; }
          .vital small { color: var(--dnd-muted); display: block; font-size: .66rem; font-weight: 800; letter-spacing: .08em; margin-top: .3rem; text-transform: uppercase; }
          .heart { align-items: center; background: linear-gradient(145deg, #b64c47, #6d2424); border-radius: 1.2rem 1.2rem 1.2rem .25rem; display: flex; font-weight: 900; height: 3.6rem; justify-content: center; transform: rotate(-45deg); width: 3.6rem; }
          .heart span { transform: rotate(45deg); }
          .shield { align-items: center; background: linear-gradient(#d3b171, #8e6e3b); clip-path: polygon(50% 0, 95% 16%, 87% 72%, 50% 100%, 13% 72%, 5% 16%); color: #241b13; display: flex; font-family: Georgia, serif; font-size: 1.5rem; font-weight: 900; height: 4rem; justify-content: center; width: 3.6rem; }
          meter { accent-color: var(--dnd-red); display: block; height: .55rem; margin-top: .45rem; width: 100%; }
          .vital-actions { display: grid; gap: .7rem; grid-column: 1 / -1; grid-template-columns: repeat(2, minmax(0, 1fr)); margin-top: .15rem; }
          .vital-action { background: linear-gradient(145deg, rgba(53,42,37,.92), rgba(27,26,29,.96)); border: 1px solid #6c5b43; border-radius: .8rem; display: grid; gap: .7rem; min-width: 0; padding: .8rem; }
          .vital-action-head { align-items: baseline; display: flex; gap: .6rem; justify-content: space-between; }
          .vital-action h4 { color: var(--dnd-gold); font-family: Georgia, serif; font-size: .92rem; letter-spacing: .06em; margin: 0; text-transform: uppercase; }
          .vital-action-note { color: var(--dnd-muted); font-size: .68rem; line-height: 1.4; margin: 0; }
          .vital-stepper { align-items: center; display: grid; gap: .5rem; grid-template-columns: 2.7rem minmax(5.5rem, 1fr) 2.7rem; }
          .vital-stepper button, .vital-choice { background: #211e20; border: 1px solid #755e40; border-radius: .55rem; color: inherit; cursor: pointer; font: inherit; font-size: .75rem; font-weight: 850; min-height: 2.6rem; padding: .4rem; }
          .vital-stepper button:hover, .vital-choice:hover { filter: brightness(1.16); }
          .vital-stepper button:disabled { cursor: not-allowed; opacity: .45; }
          .vital-stepper strong { background: #171518; border: 1px solid #655641; border-radius: .55rem; font-family: Georgia, serif; font-size: 1.2rem; padding: .52rem; text-align: center; }
          .vital-choice-grid { display: grid; gap: .45rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .vital-choice[aria-pressed='true'] { background: linear-gradient(145deg, #a94e42, #692d2a); border-color: var(--dnd-gold); color: #fff6dd; }
          .vital-action-slot { display: grid; gap: .55rem; min-height: 2.7rem; }
          .vital-expire { border-top: 1px solid rgba(216,204,182,.14); display: grid; gap: .5rem; padding-top: .65rem; }
          .abilities { display: grid; gap: .6rem; grid-template-columns: repeat(6, minmax(4.6rem, 1fr)); }
          .ability { background: linear-gradient(160deg, #343034, #211f22); border: 1px solid #6b5c49; border-radius: .8rem; color: inherit; min-height: 7.2rem; padding: .75rem .35rem; position: relative; text-align: center; }
          .ability::before { border: 1px solid rgba(224,185,104,.18); border-radius: .55rem; content: ''; inset: .28rem; pointer-events: none; position: absolute; }
          .ability-name { color: var(--dnd-gold); display: block; font-size: .68rem; font-weight: 900; letter-spacing: .13em; text-transform: uppercase; }
          .ability-score { display: block; font-family: Georgia, serif; font-size: 2.45rem; font-weight: 900; line-height: 1.25; }
          .ability-caption { color: var(--dnd-muted); display: block; font-size: .62rem; }
          .tokens { display: grid; gap: .55rem; grid-template-columns: repeat(5, minmax(5.5rem, 1fr)); }
          .token { align-items: center; background: #252326; border: 1px solid #625849; border-radius: .72rem; display: flex; flex-direction: column; justify-content: center; min-height: 5.2rem; padding: .55rem; text-align: center; }
          .token[data-ready='true'] { background: radial-gradient(circle at 50% 25%, #42694d, #23342a); border-color: #79aa82; box-shadow: inset 0 0 0 2px rgba(121,170,130,.08); }
          .token-icon { align-items: center; border: 2px solid currentColor; border-radius: 50%; display: flex; font-family: Georgia, serif; font-size: 1rem; font-weight: 900; height: 2rem; justify-content: center; margin-bottom: .3rem; width: 2rem; }
          .token strong { font-size: .66rem; letter-spacing: .04em; text-transform: uppercase; }
          .token small { color: var(--dnd-muted); font-size: .62rem; margin-top: .1rem; }
          .chips { display: flex; flex-wrap: wrap; gap: .42rem; min-height: 2rem; }
          .chip { background: #2b292d; border: 1px solid #655b50; border-radius: 999px; color: var(--dnd-ink); font-size: .72rem; padding: .4rem .65rem; }
          .chip.condition { background: #5a2c2d; border-color: #a45d58; }
          .chip.good { background: #284432; border-color: #527c5d; }
          .chip.warn { background: #5d4928; border-color: #9d7f44; }
          .stack { display: grid; gap: .85rem; }
          .group-title { color: var(--dnd-muted); font-size: .67rem; font-weight: 900; letter-spacing: .1em; margin: 0 0 .42rem; text-transform: uppercase; }
          .dossier { display: grid; gap: .8rem; }
          .dossier-stats { display: grid; gap: .55rem; grid-template-columns: repeat(4, minmax(0, 1fr)); }
          .dossier-stat { background: #29272a; border: 1px solid #5f5548; border-radius: .62rem; min-width: 0; padding: .7rem; }
          .dossier-stat span { color: var(--dnd-muted); display: block; font-size: .62rem; font-weight: 800; letter-spacing: .09em; text-transform: uppercase; }
          .dossier-stat strong { display: block; font-family: Georgia, serif; font-size: 1.15rem; margin-top: .2rem; overflow-wrap: anywhere; }
          .dossier-copy { display: grid; gap: .6rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .dossier-entry { background: linear-gradient(145deg, rgba(76,62,43,.22), rgba(38,35,38,.7)); border-left: 3px solid #8e6b3e; border-radius: .4rem; min-width: 0; padding: .7rem; }
          .dossier-entry:last-child { grid-column: 1 / -1; }
          .dossier-entry strong { color: #e7cf9b; display: block; font-size: .67rem; letter-spacing: .09em; margin-bottom: .3rem; text-transform: uppercase; }
          .dossier-entry p { color: #ddd3c0; font-family: Georgia, serif; font-size: .86rem; line-height: 1.5; margin: 0; overflow-wrap: anywhere; white-space: pre-wrap; }
          .encounter-head { align-items: center; display: flex; flex-wrap: wrap; gap: .45rem; justify-content: space-between; margin-bottom: .7rem; }
          .encounter-badge { background: #322d2d; border: 1px solid #665b50; border-radius: 999px; color: #d8ccb6; font-size: .66rem; font-weight: 800; letter-spacing: .06em; padding: .38rem .55rem; text-transform: uppercase; }
          .encounter-badge[data-state='active'] { background: #284432; border-color: #527c5d; color: #dff1df; }
          .encounter-badge[data-state='ended'] { background: #4a2b2b; border-color: #82504c; color: #f1cfca; }
          .initiative-list { display: grid; gap: .48rem; list-style: none; margin: 0; padding: 0; }
          .initiative-card { align-items: center; background: #29272a; border: 1px solid #5f5548; border-radius: .68rem; display: grid; gap: .55rem; grid-template-columns: auto minmax(0, 1fr) auto; min-width: 0; padding: .62rem; position: relative; }
          .initiative-card[aria-current='step'] { background: linear-gradient(100deg, #385443, #2a2c2b); border-color: #80ad86; box-shadow: inset .25rem 0 #9bc39e; }
          .initiative-rank { align-items: center; border: 1px solid #77664d; border-radius: 50%; color: var(--dnd-gold); display: flex; font-family: Georgia, serif; font-weight: 900; height: 2rem; justify-content: center; width: 2rem; }
          .initiative-name { font-family: Georgia, serif; font-size: .9rem; font-weight: 700; overflow-wrap: anywhere; }
          .initiative-id { color: var(--dnd-muted); display: block; font-family: ui-monospace, monospace; font-size: .58rem; margin-top: .12rem; overflow-wrap: anywhere; }
          .initiative-score { color: #f4dca3; font-family: Georgia, serif; font-size: 1.3rem; font-weight: 900; text-align: right; }
          .initiative-score small { color: var(--dnd-muted); display: block; font-family: Inter, sans-serif; font-size: .55rem; letter-spacing: .06em; text-transform: uppercase; }
          .inventory-stack { display: grid; gap: .8rem; }
          .inventory-grid { display: grid; gap: .58rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .inventory-node { display: grid; gap: .52rem; min-width: 0; }
          .inventory-children { border-left: 2px solid rgba(224,185,104,.34); display: grid; gap: .52rem; margin-left: .62rem; padding-left: .72rem; }
          .inventory-children > .group-title { margin-top: .14rem; }
          .inventory-depth { background: rgba(41,39,42,.72); border: 1px solid rgba(142,107,62,.52); border-radius: 999px; color: #d8ccb6; font-size: .56rem; justify-self: start; letter-spacing: .08em; padding: .26rem .45rem; text-transform: uppercase; }
          .item-card { background: linear-gradient(145deg, #34302b, #242326 72%); border: 1px solid #6c5b43; border-radius: .75rem; display: grid; gap: .55rem; min-width: 0; padding: .72rem; position: relative; }
          .item-card::after { border: 1px solid rgba(224,185,104,.13); border-radius: .52rem; content: ''; inset: .25rem; pointer-events: none; position: absolute; }
          .item-head { align-items: center; display: grid; gap: .55rem; grid-template-columns: auto minmax(0, 1fr) auto; }
          .item-rune { align-items: center; background: #201b18; border: 1px solid #8d7148; border-radius: .45rem; color: var(--dnd-gold); display: flex; font-family: Georgia, serif; font-size: 1rem; font-weight: 900; height: 2.3rem; justify-content: center; width: 2.3rem; }
          .item-name { font-family: Georgia, serif; font-size: .9rem; font-weight: 800; overflow-wrap: anywhere; }
          .item-id { color: var(--dnd-muted); display: block; font-family: ui-monospace, monospace; font-size: .56rem; margin-top: .1rem; overflow-wrap: anywhere; }
          .item-count { color: #f4dca3; font-family: Georgia, serif; font-size: 1.12rem; font-weight: 900; }
          .item-meta { display: flex; flex-wrap: wrap; gap: .32rem; }
          .item-tag { background: #29272a; border: 1px solid #5f5548; border-radius: 999px; color: #d8ccb6; font-size: .6rem; padding: .3rem .48rem; }
          .item-tag.equipped { background: #284432; border-color: #527c5d; color: #dff1df; }
          .item-tag.warn { background: #5d4928; border-color: #9d7f44; color: #f2ddb1; }
          .item-facts { border-top: 1px solid rgba(216,204,182,.13); display: grid; gap: .35rem; grid-template-columns: repeat(2, minmax(0, 1fr)); padding-top: .5rem; }
          .item-fact { color: #d8ccb6; display: grid; font-size: .68rem; gap: .08rem; min-width: 0; }
          .item-fact small { color: #9d927f; font-size: .55rem; letter-spacing: .06em; text-transform: uppercase; }
          .item-source { color: #9d927f; font-size: .58rem; line-height: 1.4; overflow-wrap: anywhere; }
          .item-actions { border-top: 1px solid rgba(216,204,182,.14); display: grid; gap: .55rem; padding-top: .65rem; position: relative; z-index: 1; }
          .item-actions-head { align-items: baseline; display: flex; gap: .5rem; justify-content: space-between; }
          .item-actions-head strong { color: var(--dnd-gold); font-family: Georgia, serif; font-size: .8rem; letter-spacing: .05em; text-transform: uppercase; }
          .item-actions-head span { color: var(--dnd-muted); font-size: .62rem; }
          .item-mode-grid { display: grid; gap: .42rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .item-mode { background: #211e20; border: 1px solid #755e40; border-radius: .52rem; color: inherit; cursor: pointer; font: inherit; font-size: .7rem; font-weight: 850; min-height: 2.45rem; padding: .38rem; }
          .item-mode[aria-pressed='true'] { background: linear-gradient(145deg, #a94e42, #692d2a); border-color: var(--dnd-gold); color: #fff6dd; }
          .item-mode:hover { filter: brightness(1.16); }
          .item-action-slot { display: grid; gap: .5rem; min-height: 2.7rem; }
          .item-operation { border-top: 1px solid rgba(216,204,182,.14); display: grid; gap: .58rem; padding-top: .65rem; position: relative; z-index: 1; }
          .item-operation summary { color: #e7cf9b; cursor: pointer; font-size: .7rem; font-weight: 850; letter-spacing: .05em; text-transform: uppercase; }
          .item-operation-grid { display: grid; gap: .5rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .item-operation label { color: var(--dnd-muted); display: grid; font-size: .62rem; font-weight: 800; gap: .28rem; letter-spacing: .04em; text-transform: uppercase; }
          .item-operation select, .item-operation input { background: #171518; border: 1px solid #655641; border-radius: .5rem; color: inherit; font: inherit; min-height: 2.4rem; min-width: 0; padding: .45rem; width: 100%; }
          .item-operation-note { color: var(--dnd-muted); font-size: .65rem; line-height: 1.4; margin: 0; }
          .item-operation-note.warn { color: #ffd0b0; }
          .item-stack-stepper { align-items: center; display: grid; gap: .42rem; grid-template-columns: 2.45rem minmax(5rem, 1fr) 2.45rem; }
          .item-stack-stepper button { background: #211e20; border: 1px solid #755e40; border-radius: .5rem; color: inherit; cursor: pointer; font: inherit; font-weight: 850; min-height: 2.4rem; }
          .item-stack-stepper button:disabled { cursor: not-allowed; opacity: .45; }
          .item-stack-stepper strong { background: #171518; border: 1px solid #655641; border-radius: .5rem; font-family: Georgia, serif; padding: .48rem; text-align: center; }
          .item-use-list { display: grid; gap: .55rem; }
          .inventory-boundary { background: rgba(94,74,42,.24); border-left: 3px solid #9d7f44; color: #d8ccb6; font-size: .68rem; line-height: 1.45; padding: .55rem .65rem; }
          .contained-card { align-items: center; background: #29272a; border: 1px dashed #655b50; border-radius: .62rem; display: flex; gap: .55rem; justify-content: space-between; min-width: 0; padding: .6rem; }
          .contained-card span { min-width: 0; overflow-wrap: anywhere; }
          .contained-card small { color: var(--dnd-muted); display: block; font-family: ui-monospace, monospace; font-size: .56rem; }
          .speed-list { display: grid; gap: .45rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .speed-item { align-items: center; background: #29272a; border-left: 3px solid var(--dnd-gold); border-radius: .35rem; display: flex; justify-content: space-between; padding: .55rem .65rem; }
          .speed-item span { color: var(--dnd-muted); font-size: .72rem; text-transform: capitalize; }
          .speed-item strong { font-family: Georgia, serif; }
          .location-card { background: radial-gradient(circle at 12% 10%, rgba(224,185,104,.16), transparent 36%), linear-gradient(145deg, #352d29, #222125); border: 1px solid #786246; border-radius: .8rem; display: grid; gap: .65rem; min-height: 10rem; padding: 1rem; position: relative; }
          .location-card::after { border: 1px solid rgba(224,185,104,.13); border-radius: .56rem; content: ''; inset: .28rem; pointer-events: none; position: absolute; }
          .location-kind { color: var(--dnd-gold); font-size: .65rem; font-weight: 900; letter-spacing: .14em; text-transform: uppercase; }
          .location-name { font-family: Georgia, serif; font-size: clamp(1.35rem, 3vw, 2rem); line-height: 1.05; margin: 0; }
          .location-summary { color: #ddd3c0; font-family: Georgia, serif; font-size: .9rem; line-height: 1.55; margin: 0; white-space: pre-wrap; }
          .location-meta { color: var(--dnd-muted); font-size: .65rem; font-weight: 800; letter-spacing: .07em; text-transform: uppercase; }
          .people-view { display: grid; gap: .8rem; }
          .people-switcher { display: grid; gap: .5rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .person-card { align-items: center; background: linear-gradient(145deg, #302d31, #222024); border: 1px solid #625849; border-radius: .68rem; color: inherit; cursor: pointer; display: grid; gap: .55rem; grid-template-columns: auto minmax(0, 1fr); min-height: 4.3rem; padding: .62rem; text-align: left; }
          .person-card:hover { filter: brightness(1.12); }
          .person-card[aria-pressed='true'] { background: linear-gradient(120deg, #385443, #292b2a); border-color: #80ad86; box-shadow: inset .22rem 0 #9bc39e; }
          .person-mark { align-items: center; background: #211b1c; border: 1px solid #806a42; border-radius: 50%; color: var(--dnd-gold); display: flex; font-family: Georgia, serif; font-size: 1rem; font-weight: 900; height: 2.55rem; justify-content: center; text-transform: uppercase; width: 2.55rem; }
          .person-name { display: block; font-family: Georgia, serif; font-size: .88rem; font-weight: 800; overflow-wrap: anywhere; }
          .person-role { color: var(--dnd-muted); display: block; font-size: .6rem; letter-spacing: .06em; margin-top: .12rem; text-transform: uppercase; }
          .person-detail { background: linear-gradient(145deg, rgba(76,62,43,.22), rgba(38,35,38,.7)); border-left: 3px solid #8e6b3e; border-radius: .45rem; display: grid; gap: .4rem; min-height: 6rem; padding: .8rem; }
          .person-detail h4 { color: #f4dca3; font-family: Georgia, serif; font-size: 1.12rem; margin: 0; }
          .person-detail p { color: #ddd3c0; font-size: .74rem; line-height: 1.5; margin: 0; white-space: pre-wrap; }
          .scene-boundary { background: rgba(94,74,42,.24); border-left: 3px solid #9d7f44; color: #d8ccb6; font-size: .68rem; line-height: 1.45; padding: .55rem .65rem; }
          .knowledge-view { display: grid; gap: .85rem; }
          .knowledge-tools { align-items: end; background: linear-gradient(145deg, #352d29, #222125); border: 1px solid #786246; border-radius: .78rem; display: grid; gap: .65rem; grid-template-columns: minmax(12rem, 1fr) auto; padding: .75rem; }
          .knowledge-search { color: var(--dnd-muted); display: grid; font-size: .65rem; font-weight: 850; gap: .32rem; letter-spacing: .08em; text-transform: uppercase; }
          .knowledge-search input { background: #171518; border: 1px solid #755e40; border-radius: .55rem; color: var(--dnd-ink); font: inherit; min-height: 2.65rem; padding: .55rem .7rem; width: 100%; }
          .knowledge-search input:focus-visible { outline: 3px solid #f3d797; outline-offset: 2px; }
          .knowledge-kinds { display: flex; flex-wrap: wrap; gap: .38rem; }
          .knowledge-kind { background: #211e20; border: 1px solid #755e40; border-radius: 999px; color: var(--dnd-ink); cursor: pointer; font: inherit; font-size: .66rem; font-weight: 850; min-height: 2.35rem; padding: .42rem .68rem; }
          .knowledge-kind[aria-pressed='true'] { background: linear-gradient(145deg, #a94e42, #692d2a); border-color: var(--dnd-gold); color: #fff6dd; }
          .knowledge-ledger { display: grid; gap: .7rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .knowledge-card { background: radial-gradient(circle at 8% 5%, rgba(224,185,104,.12), transparent 34%), linear-gradient(145deg, #34302b, #222125); border: 1px solid #6c5b43; border-radius: .76rem; display: grid; gap: .55rem; min-width: 0; padding: .85rem; position: relative; }
          .knowledge-card::after { border: 1px solid rgba(224,185,104,.12); border-radius: .5rem; content: ''; inset: .26rem; pointer-events: none; position: absolute; }
          .knowledge-card-head { align-items: start; display: flex; gap: .55rem; justify-content: space-between; }
          .knowledge-title { color: #f4dca3; font-family: Georgia, serif; font-size: 1rem; line-height: 1.25; margin: 0; }
          .knowledge-stance { background: #284432; border: 1px solid #527c5d; border-radius: 999px; color: #dff1df; flex: 0 0 auto; font-size: .58rem; font-weight: 900; letter-spacing: .06em; padding: .3rem .48rem; text-transform: uppercase; }
          .knowledge-stance[data-stance='suspected'], .knowledge-stance[data-stance='doubted'] { background: #5d4928; border-color: #9d7f44; color: #f2ddb1; }
          .knowledge-copy { color: #ddd3c0; font-family: Georgia, serif; font-size: .82rem; line-height: 1.55; margin: 0; white-space: pre-wrap; }
          .knowledge-mark { color: var(--dnd-muted); font-size: .58rem; font-weight: 850; letter-spacing: .1em; text-transform: uppercase; }
          .empty { align-items: center; border: 1px dashed #655b50; border-radius: .6rem; color: var(--dnd-muted); display: flex; font-size: .76rem; min-height: 3.2rem; padding: .7rem; }
          .unknown { color: var(--dnd-muted); font-family: Georgia, serif; font-size: 1rem; font-style: italic; }
          [hidden] { display: none !important; }
          @media (max-width: 900px) {
            .dashboard, .combat-grid, .scene-grid { grid-template-columns: 1fr; }
            .vitals { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .vital-actions { grid-template-columns: 1fr; }
            .abilities { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .tokens { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .dossier-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .inventory-grid, .knowledge-ledger { grid-template-columns: 1fr; }
            .people-switcher { grid-template-columns: 1fr; }
          }
          @media (max-width: 560px) {
            .banner { grid-template-columns: auto minmax(0, 1fr); min-height: auto; }
            .crest { height: 3.7rem; width: 3.7rem; }
            .level { grid-column: 1 / -1; justify-self: start; }
            .toolbar, .knowledge-tools { grid-template-columns: 1fr; }
            .view-tab { flex-direction: column; gap: .32rem; min-height: 4.5rem; padding: .48rem .25rem; }
            .view-tab-label { font-size: .67rem; letter-spacing: .05em; }
            .vitals { grid-template-columns: 1fr 1fr; }
            .vital-actions { grid-template-columns: 1fr; }
            .abilities { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .tokens { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .dossier-copy { grid-template-columns: 1fr; }
            .dossier-entry:last-child { grid-column: auto; }
          }
          @media (prefers-reduced-motion: reduce) { *, *::before, *::after { scroll-behavior: auto !important; } }
          @media (forced-colors: active) { .shield, .heart, .ability, .token { border: 2px solid CanvasText; } }
        `;

        this._shell = this._element('section', 'shell');
        this._shell.setAttribute('aria-label', 'D&D 2024 game viewport');
        const banner = this._element('header', 'banner');
        const crest = this._element('div', 'crest', 'D20');
        crest.setAttribute('aria-hidden', 'true');
        const identity = this._element('div', 'identity');
        identity.append(this._element('p', 'eyebrow', 'D&D 2024 · Adventurer dossier'));
        this._name = this._element('h2', '', 'Choose an adventurer');
        this._scope = this._element('p', 'scope', 'Waiting for an exact game scope…');
        identity.append(this._name, this._scope);
        this._level = this._element('span', 'level', 'Level —');
        banner.append(crest, identity, this._level);

        const toolbar = this._element('div', 'toolbar');
        const campaignLabel = this._element('label', '', 'Campaign');
        this._campaignSelect = document.createElement('select');
        this._campaignSelect.setAttribute('aria-label', 'Campaign');
        this._campaignSelect.addEventListener('change', () => {
          this.removeAttribute('entity-id');
          this.removeAttribute('encounter-id');
          this.setAttribute('campaign-id', this._campaignSelect.value);
        });
        campaignLabel.append(this._campaignSelect);
        const stateLabel = this._element('label', '', 'Registered campaign space');
        this._stateSpaceSelect = document.createElement('select');
        this._stateSpaceSelect.setAttribute('aria-label', 'Campaign state space');
        this._stateSpaceSelect.addEventListener('change', () => {
          this.removeAttribute('campaign-id');
          this.removeAttribute('entity-id');
          this.removeAttribute('encounter-id');
          this.setAttribute('state-space-id', this._stateSpaceSelect.value);
        });
        stateLabel.append(this._stateSpaceSelect);
        const entityLabel = this._element('label', '', 'Adventurer or creature');
        this._entitySelect = document.createElement('select');
        this._entitySelect.setAttribute('aria-label', 'Adventurer or creature');
        this._entitySelect.addEventListener('change', () => this.setAttribute('entity-id', this._entitySelect.value));
        entityLabel.append(this._entitySelect);
        const encounterLabel = this._element('label', '', 'Encounter');
        this._encounterSelect = document.createElement('select');
        this._encounterSelect.setAttribute('aria-label', 'Recorded campaign encounter');
        this._encounterSelect.addEventListener('change', () =>
          this.setAttribute('encounter-id', this._encounterSelect.value));
        encounterLabel.append(this._encounterSelect);
        this._refresh = this._element('button', 'refresh', 'Refresh state');
        this._refresh.type = 'button';
        this._refresh.addEventListener('click', () => this._load());
        toolbar.append(campaignLabel, stateLabel, entityLabel, encounterLabel, this._refresh);

        this._status = this._element('p', 'status', 'Loading current game state…');
        this._status.setAttribute('role', 'status');
        this._status.setAttribute('aria-live', 'polite');

        const viewNavigation = this._element('nav', 'view-navigation');
        viewNavigation.setAttribute('aria-label', 'Player viewport');
        const viewTabs = this._element('div', 'view-tabs');
        viewTabs.setAttribute('role', 'tablist');
        viewTabs.setAttribute('aria-label', 'Game information views');
        [
          ['character', 'Character', '◆'],
          ['scene', 'Scene', '◉'],
          ['knowledge', 'Knowledge', '✦'],
          ['campaign', 'Campaign', '⌂'],
          ['combat', 'Combat', '⚔']
        ].forEach(([key, label, icon]) => viewTabs.append(this._viewTab(key, label, icon)));
        viewNavigation.append(viewTabs);

        const characterView = document.createElement('dnd2024-character-sheet');
        this._configureViewPanel(characterView, 'character');
        characterView.className = 'view-panel dashboard';
        const characterMain = this._element('div', 'main');
        const characterSide = this._element('aside', 'side');
        const campaignView = this._element('div', 'view-panel single-view');
        this._configureViewPanel(campaignView, 'campaign');
        const sceneView = this._element('div', 'view-panel scene-grid');
        this._configureViewPanel(sceneView, 'scene');
        const knowledgeView = this._element('div', 'view-panel single-view');
        this._configureViewPanel(knowledgeView, 'knowledge');
        const combatView = this._element('div', 'view-panel combat-grid');
        this._configureViewPanel(combatView, 'combat');
        this._campaignPanel = this._panel('Campaign dossier', 'Registered campaign state');
        this._vitals = this._panel('Vitals', 'Current stored values');
        this._dossier = this._panel('Character dossier', 'Recorded identity · no inferred choices');
        this._abilities = this._panel('Ability scores', 'Server state · no local modifiers');
        this._inventoryPanel = this._panel('Inventory', 'Bounded nested contents · contextual actions');
        this._turn = this._panel('Turn resources', 'Spendable encounter resources');
        this._actions = this._panel('Action table', 'Server roll · review before execution');
        this._conditions = this._panel('Conditions', 'Active state');
        this._speed = this._panel('Movement', 'Base and remaining speed');
        this._proficiencies = this._panel('Proficiencies', 'Known training');
        this._mitigation = this._panel('Damage response', 'Resistance, immunity, vulnerability');
        this._encounter = this._panel('Encounter initiative', 'Recorded order · reviewed turn controls');
        this._location = this._panel('Current location', 'Exact recorded world presence');
        this._scenePeople = this._panel('People here', 'Switch between present actors');
        this._knowledgePanel = this._panel('Remembered lore', 'Only information available to this player');
        characterMain.append(this._vitals.panel, this._dossier.panel, this._abilities.panel,
          this._inventoryPanel.panel, this._actions.panel);
        characterSide.append(this._conditions.panel, this._speed.panel,
          this._proficiencies.panel, this._mitigation.panel);
        characterView.append(characterMain, characterSide);
        sceneView.append(this._location.panel, this._scenePeople.panel);
        knowledgeView.append(this._knowledgePanel.panel);
        campaignView.append(this._campaignPanel.panel);
        combatView.append(this._encounter.panel, this._turn.panel);
        this._shell.append(banner, toolbar, this._status, viewNavigation,
          characterView, sceneView, knowledgeView, campaignView, combatView);
        this.shadowRoot.append(style, this._shell);
        this._selectView('character');
        this._renderUnknownPanels('Choose a current entity to reveal its game state.');
      }

      _viewTab(key, label, icon) {
        const button = this._element('button', 'view-tab');
        button.type = 'button';
        button.id = `dnd2024-view-tab-${key}`;
        button.setAttribute('role', 'tab');
        button.setAttribute('aria-controls', `dnd2024-view-${key}`);
        const iconNode = this._element('span', 'view-tab-icon', icon);
        iconNode.setAttribute('aria-hidden', 'true');
        button.append(iconNode, this._element('span', 'view-tab-label', label));
        button.addEventListener('click', () => this._selectView(key));
        button.addEventListener('keydown', event => this._handleViewTabKey(event, key));
        this._viewButtons.set(key, button);
        return button;
      }

      _configureViewPanel(panel, key) {
        panel.id = `dnd2024-view-${key}`;
        panel.setAttribute('role', 'tabpanel');
        panel.setAttribute('aria-labelledby', `dnd2024-view-tab-${key}`);
        panel.tabIndex = 0;
        this._viewPanels.set(key, panel);
      }

      _selectView(key, moveFocus = false) {
        if (!this._viewButtons.has(key) || !this._viewPanels.has(key)) return;
        this._selectedView = key;
        this._viewButtons.forEach((button, candidate) => {
          const selected = candidate === key;
          button.setAttribute('aria-selected', String(selected));
          button.tabIndex = selected ? 0 : -1;
        });
        this._viewPanels.forEach((panel, candidate) => { panel.hidden = candidate !== key; });
        if (moveFocus) this._viewButtons.get(key).focus();
      }

      _handleViewTabKey(event, key) {
        const order = [...this._viewButtons.keys()];
        const current = order.indexOf(key);
        let next = current;
        if (event.key === 'ArrowRight') next = (current + 1) % order.length;
        else if (event.key === 'ArrowLeft') next = (current - 1 + order.length) % order.length;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = order.length - 1;
        else return;
        event.preventDefault();
        this._selectView(order[next], true);
      }

      _panel(title, note) {
        const panel = this._element('section', 'panel');
        const heading = this._element('div', 'panel-heading');
        heading.append(this._element('h3', '', title), this._element('span', 'panel-note', note));
        const body = this._element('div', 'panel-body');
        panel.append(heading, body);
        return {panel, body};
      }

      _element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
      }

      _setStatus(message, error = false) {
        this._status.textContent = message;
        this._status.dataset.error = String(error);
        this._status.dataset.errorCode = '';
      }

      _emit(type, detail) {
        this.dispatchEvent(new CustomEvent(type, {detail, bubbles: true, composed: true}));
      }

      async _load() {
        if (this._request) this._request.abort();
        const request = new AbortController();
        this._request = request;
        this._refresh.disabled = true;
        this._setStatus('Loading registered campaign spaces…');
        this._emit('dnd2024-progress', {phase: 'state-spaces'});
        try {
          const root = `/api/applications/${encodeURIComponent(this.applicationId)}/state-spaces`;
          const candidates = await this._readAll(root, request.signal);
          this._stateSpaces = candidates;
          if (request !== this._request) return;
          this._fillSelect(this._stateSpaceSelect, this._stateSpaces, 'stateSpaceId', 'stateSpaceId',
            this.getAttribute('state-space-id'), 'No registered campaign spaces');
          if (!this._stateSpaceSelect.value) {
            this._stateSpaceEntities = [];
            this._entityComponentSummaries = new Map();
            this._campaigns = [];
            this._entities = [];
            this._clearEncounterState();
            this._inventory = {status: 'missing', contents: [], boundary: null};
            this._clearCurrentContext();
            this._knowledge = {status: 'missing', entries: []};
            this._fillSelect(this._entitySelect, [], 'entityId', 'name', null, 'No entities');
            this._fillSelect(this._campaignSelect, [], 'entityId', 'label', null, 'No campaigns');
            this._fillSelect(this._encounterSelect, [], 'entityId', 'name', null, 'No recorded encounters');
            this._renderUnknownPanels('No registered D&D campaign space is available.');
            this._setStatus('No registered D&D campaign space is available.');
            return;
          }
          await this._loadCampaigns(request);
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('Game state is unavailable. Check access and try again.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
      }

      async _loadCampaigns(existingRequest) {
        let request = existingRequest;
        if (!request) {
          if (this._request) this._request.abort();
          request = new AbortController();
          this._request = request;
          this._refresh.disabled = true;
        }
        const stateSpaceId = this.selectedStateSpaceId;
        if (!stateSpaceId) return;
        this._setStatus('Finding registered campaigns…');
        this._emit('dnd2024-progress', {phase: 'campaigns', stateSpaceId});
        try {
          const root = this._applicationRoot(stateSpaceId) + '/entities';
          const candidates = await this._readAll(root, request.signal);
          this._stateSpaceEntities = candidates;
          this._entityComponentSummaries = new Map();
          this._campaigns = await this._findCampaigns(stateSpaceId, candidates, request.signal);
          if (request !== this._request) return;
          this._fillSelect(this._campaignSelect, this._campaigns, 'entityId', 'label',
            this.getAttribute('campaign-id'), 'No registered campaigns');
          if (!this._campaignSelect.value) {
            this._entities = [];
            this._clearEncounterState();
            this._fillSelect(this._entitySelect, [], 'entityId', 'name', null, 'No campaign actors');
            this._fillSelect(this._encounterSelect, [], 'entityId', 'name', null, 'No recorded encounters');
            this._entity = null;
            this._components = new Map();
            this._inventory = {status: 'missing', contents: [], boundary: null};
            this._clearCurrentContext();
            this._knowledge = {status: 'missing', entries: []};
            this._renderUnknownPanels('No registered campaign root exists in this campaign space.');
            this._setStatus('No registered campaign root exists in this campaign space.');
            return;
          }
          await this._loadCampaign(request);
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('Campaigns are unavailable for this campaign space.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
      }

      async _loadCampaign(existingRequest) {
        let request = existingRequest;
        if (!request) {
          if (this._request) this._request.abort();
          request = new AbortController();
          this._request = request;
          this._refresh.disabled = true;
        }
        const stateSpaceId = this.selectedStateSpaceId;
        const campaignId = this.selectedCampaignId;
        if (!stateSpaceId || !campaignId) return;
        this._setStatus('Loading selected campaign…');
        this._emit('dnd2024-progress', {phase: 'campaign', stateSpaceId, campaignId});
        try {
          const campaignRoot = this._applicationRoot(stateSpaceId) + `/entities/${encodeURIComponent(campaignId)}`;
          const campaign = await this._readJson(campaignRoot +
            `/components/${encodeURIComponent(DND2024_CAMPAIGN_ROOT_COMPONENT)}`, request.signal);
          this._campaign = this._componentValue(campaign);
          this._renderCampaign();
          this._knowledge = {status: 'loading', entries: []};
          this._renderKnowledge();
          const [actionsAvailable] = await Promise.all([
            this._isPlayableStateSpace({stateSpaceId}, request.signal),
            this._loadKnowledge(campaignId, request.signal)
          ]);
          this._actionsAvailable = actionsAvailable;
          await this._loadCampaignActors(request);
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('This campaign’s recorded state is unavailable.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
      }

      async _loadCampaignActors(request) {
        const stateSpaceId = this.selectedStateSpaceId;
        const campaignId = this.selectedCampaignId;
        const participations = await this._readRelationshipsForKinds(
          stateSpaceId, campaignId, DND2024_CAMPAIGN_PARTICIPATION_KINDS, request.signal);
        const actorLinks = await Promise.all(participations.map(async participation => {
          const status = await this._readJson(this._applicationRoot(stateSpaceId) +
            `/entities/${encodeURIComponent(participation.toEntityId)}/components/${encodeURIComponent(DND2024_PARTICIPATION_COMPONENT)}`,
            request.signal);
          const value = this._componentValue(status);
          if (!value || value.status !== 'active') return null;
          const actors = await this._readRelationshipsForKinds(stateSpaceId, participation.toEntityId,
            DND2024_PARTICIPATION_ACTOR_KINDS, request.signal);
          return actors.length === 1 ? actors[0].toEntityId : null;
        }));
        const actorIds = actorLinks.filter(value => typeof value === 'string');
        const actors = await Promise.all(actorIds.map(async actorId => {
          try {
            const root = this._applicationRoot(stateSpaceId) + `/entities/${encodeURIComponent(actorId)}`;
            const knownSummaries = this._entityComponentSummaries.get(actorId);
            const [entity, summaries] = await Promise.all([
              this._readJson(root, request.signal), knownSummaries || this._readAll(root + '/components', request.signal)
            ]);
            const ids = new Set(summaries.map(item => item?.qualifiedTypeId));
            return (DND2024_CHARACTER_COMPONENTS.every(id => ids.has(id)) ||
              ids.has(DND2024_LEGACY_CHARACTER_COMPONENT)) ? entity : null;
          } catch (error) {
            if (error.name === 'AbortError') throw error;
            return null;
          }
        }));
        if (request !== this._request) return;
        this._entities = actors.filter(Boolean);
        this._fillSelect(this._entitySelect, this._entities, 'entityId', 'name',
          this.getAttribute('entity-id'), 'No campaign actors');
        this._encounters = await this._findRecordedEncounters(
          stateSpaceId, this._stateSpaceEntities, request.signal);
        if (request !== this._request) return;
        this._fillSelect(this._encounterSelect, this._encounters, 'entityId', 'name',
          this.getAttribute('encounter-id'), 'No recorded encounters');
        if (!this._entitySelect.value) {
          this._entity = null;
          this._components = new Map();
          this._inventory = {status: 'missing', contents: [], boundary: null};
          this._clearCurrentContext();
          await this._loadSelectedEncounter(stateSpaceId, request.signal);
          this._renderUnknownPanels('No playable character record is linked to this campaign.');
          this._renderCampaign();
          this._renderEncounter();
          this._setStatus('Campaign loaded; no playable character record is linked.');
          return;
        }
        await this._loadEntity(request);
      }

      async _loadKnowledge(campaignId, signal) {
        try {
          const root = `/api/applications/${encodeURIComponent(this.applicationId)}` +
            `/campaigns/${encodeURIComponent(campaignId)}/knowledge`;
          const result = await this._readJson(root, signal);
          if (!result || !['ready', 'empty'].includes(result.status) || !Array.isArray(result.entries)) {
            throw new Error('KNOWLEDGE_INVALID_RESPONSE');
          }
          const entries = result.entries.filter(value => value &&
            typeof value.text === 'string' && value.text.trim() &&
            typeof value.stance === 'string' && value.stance.trim() &&
            typeof value.presentationKind === 'string' && value.presentationKind.trim())
            .map(value => ({
              text: value.text.trim(),
              stance: value.stance.trim(),
              presentationKind: value.presentationKind.trim()
            }));
          this._knowledge = {status: entries.length ? 'ready' : 'empty', entries};
        } catch (error) {
          if (error.name === 'AbortError') throw error;
          this._knowledge = {status: 'unavailable', entries: []};
        }
        this._renderKnowledge();
      }

      async _loadEntity(existingRequest) {
        let request = existingRequest;
        if (!request) {
          if (this._request) this._request.abort();
          request = new AbortController();
          this._request = request;
          this._refresh.disabled = true;
        }
        const stateSpaceId = this.selectedStateSpaceId;
        const entityId = this.selectedEntityId;
        if (!stateSpaceId || !entityId) return;
        this._setStatus('Reading the latest character state…');
        this._emit('dnd2024-progress', {phase: 'components', stateSpaceId, entityId});
        try {
          const entityRoot = this._applicationRoot(stateSpaceId) + `/entities/${encodeURIComponent(entityId)}`;
          const [entity, summaries] = await Promise.all([
            this._readJson(entityRoot, request.signal),
            this._readAll(entityRoot + '/components', request.signal)
          ]);
          if (request !== this._request) return;
          const wantedComponentIds = [...Object.values(DND2024_COMPONENTS), DND2024_LEGACY_CHARACTER_COMPONENT];
          const wanted = summaries.filter(item => wantedComponentIds.includes(item.qualifiedTypeId));
          const settled = await Promise.allSettled(wanted.map(async summary => [
            summary.qualifiedTypeId,
            await this._readJson(entityRoot + `/components/${encodeURIComponent(summary.qualifiedTypeId)}`, request.signal)
          ]));
          if (request !== this._request) return;
          this._entity = entity;
          this._components = new Map();
          for (let index = 0; index < settled.length; index += 1) {
            const result = settled[index];
            if (result.status === 'fulfilled') this._components.set(result.value[0], result.value[1]);
            else this._components.set(wanted[index].qualifiedTypeId, {});
          }
          try {
            this._emit('dnd2024-progress', {phase: 'inventory', stateSpaceId, entityId});
            this._inventory = await this._loadInventory(stateSpaceId, entityId, request.signal);
          } catch (error) {
            if (error.name === 'AbortError') throw error;
            this._inventory = {status: 'unavailable', contents: [], boundary: null,
              code: error.code || error.message || 'INVENTORY_UNAVAILABLE'};
          }
          try {
            this._emit('dnd2024-progress', {phase: 'scene', stateSpaceId, entityId});
            this._currentContext = await this._loadCurrentContext(stateSpaceId, entityId, request.signal);
          } catch (error) {
            if (error.name === 'AbortError') throw error;
            this._currentContext = {status: 'unavailable', location: null, people: [], boundary: null,
              code: error.code || error.message || 'CURRENT_CONTEXT_UNAVAILABLE'};
          }
          await this._loadSelectedEncounter(stateSpaceId, request.signal);
          if (request !== this._request) return;
          this._renderState();
          const failed = settled.filter(result => result.status === 'rejected').length;
          const inventoryFailed = this._inventory.status === 'unavailable';
          const contextFailed = this._currentContext.status === 'unavailable';
          this._setStatus(failed || inventoryFailed || contextFailed
            ? `Current state loaded; ${failed + (inventoryFailed ? 1 : 0) + (contextFailed ? 1 : 0)} panel value unavailable.`
            : 'Current state loaded.');
          this._emit('dnd2024-progress', {phase: 'ready', stateSpaceId, entityId, componentCount: this._components.size});
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('This entity’s current state is unavailable.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
      }

      _applicationRoot(stateSpaceId) {
        return `/api/applications/${encodeURIComponent(this.applicationId)}/state-spaces/${encodeURIComponent(stateSpaceId)}`;
      }

      async _findPlayableStateSpaces(candidates, signal) {
        const available = await Promise.all(candidates.map(candidate =>
          this._isPlayableStateSpace(candidate, signal)));
        return candidates.filter((_, index) => available[index]);
      }

      async _isPlayableStateSpace(candidate, signal) {
        if (!candidate || typeof candidate.stateSpaceId !== 'string' || !candidate.stateSpaceId.trim()) return false;
        try {
          await this._readJson(this._applicationRoot(candidate.stateSpaceId) +
            `/mechanics/${encodeURIComponent('mechanic.dnd2024.dice')}`, signal);
          return true;
        } catch (error) {
          if (error.name === 'AbortError') throw error;
          return false;
        }
      }

      async _findCampaigns(stateSpaceId, candidates, signal) {
        const matches = await this._findEntitiesWithComponent(
          stateSpaceId, candidates, DND2024_CAMPAIGN_ROOT_COMPONENT, signal);
        return matches.map(item => ({...item, label: item.name || item.entityId}));
      }

      async _findRecordedEncounters(stateSpaceId, candidates, signal) {
        const campaignActorIds = new Set(this._entities.map(item => item.entityId));
        if (!campaignActorIds.size) return [];
        const matches = new Array(candidates.length);
        let nextIndex = 0;
        const worker = async () => {
          while (nextIndex < candidates.length) {
            const index = nextIndex;
            nextIndex += 1;
            const candidate = candidates[index];
            if (!candidate || typeof candidate.entityId !== 'string' || !candidate.entityId.trim()) continue;
            const entityRoot = this._applicationRoot(stateSpaceId) +
              `/entities/${encodeURIComponent(candidate.entityId)}`;
            try {
              const summaries = this._entityComponentSummaries.get(candidate.entityId) ||
                await this._readAll(entityRoot + '/components', signal);
              this._entityComponentSummaries.set(candidate.entityId, summaries);
              if (!summaries.some(item => item?.qualifiedTypeId === DND2024_COMPONENTS.encounterOrder)) continue;
              const detail = await this._readJson(entityRoot +
                `/components/${encodeURIComponent(DND2024_COMPONENTS.encounterOrder)}`, signal);
              const order = this._componentValue(detail);
              if (!this._validEncounterOrder(order)) continue;
              const rosterPage = await this._loadInventoryPage(stateSpaceId, candidate.entityId, 100, signal);
              if (!this._encounterRosterMatches(order, rosterPage, candidate.entityId)) continue;
              const candidateParticipantIds = order.order.map(item => item.participantId);
              if (!candidateParticipantIds.every(id => campaignActorIds.has(id))) continue;
              matches[index] = candidate;
            } catch (error) {
              if (error.name === 'AbortError') throw error;
            }
          }
        };
        await Promise.all(Array.from({length: Math.min(DND2024_CHARACTER_FILTER_CONCURRENCY, candidates.length)},
          () => worker()));
        return matches.filter(Boolean);
      }

      _validEncounterOrder(value) {
        if (!this._closedInventoryValue(value, ['order', 'sourceRef']) ||
          !Array.isArray(value.order) || value.order.length < 1 || value.order.length > 100 ||
          !this._closedInventoryValue(value.sourceRef, ['sourceId', 'locator']) ||
          value.sourceRef.sourceId !== 'source.dnd2024.srd-5.2.1' ||
          value.sourceRef.locator !== 'Playing the Game > Combat > The Order of Combat > Initiative') return false;
        const seen = new Set();
        for (const item of value.order) {
          if (!this._closedInventoryValue(item, ['participantId', 'initiative']) ||
            typeof item.participantId !== 'string' || item.participantId.length < 1 ||
            item.participantId.length > 200 || !Number.isSafeInteger(item.initiative) ||
            seen.has(item.participantId)) return false;
          seen.add(item.participantId);
        }
        return true;
      }

      _validEncounterTurn(value, participantCount) {
        return this._closedInventoryValue(value, ['status', 'round', 'turnIndex', 'sourceRef']) &&
          (value.status === 'active' || value.status === 'ended') &&
          Number.isSafeInteger(value.round) && value.round >= 1 &&
          Number.isInteger(value.turnIndex) && value.turnIndex >= 0 && value.turnIndex < participantCount &&
          this._closedInventoryValue(value.sourceRef, ['sourceId', 'locator']) &&
          value.sourceRef.sourceId === 'source.dnd2024.srd-5.2.1' &&
          value.sourceRef.locator === 'Playing the Game > Combat > The Order of Combat';
      }

      _encounterRosterMatches(order, page, encounterId) {
        if (!this._validEncounterOrder(order) || !page || !Array.isArray(page.items) ||
          page.items.length !== order.order.length ||
          (typeof page.nextCursor === 'string' && page.nextCursor.length > 0)) return false;
        const wanted = new Set(order.order.map(item => item.participantId));
        const seen = new Set();
        for (const containment of page.items) {
          if (!this._isInventoryContainment(containment, encounterId) ||
            !wanted.has(containment.containedEntityId) || seen.has(containment.containedEntityId)) return false;
          seen.add(containment.containedEntityId);
        }
        return seen.size === wanted.size;
      }

      _clearEncounterState() {
        this._encounters = [];
        this._encounterEntity = null;
        this._encounterComponents = new Map();
        this._encounterStatus = 'missing';
      }

      _clearCurrentContext() {
        this._currentContext = {status: 'missing', location: null, people: [], boundary: null};
        this._selectedScenePersonId = '';
      }

      async _loadSelectedEncounter(stateSpaceId, signal) {
        this._encounterEntity = null;
        this._encounterComponents = new Map();
        this._encounterStatus = 'missing';
        const encounterId = this.selectedEncounterId;
        if (!encounterId) return;
        if (!this._encounters.some(item => item.entityId === encounterId)) {
          this._encounterStatus = 'unavailable';
          return;
        }
        const entityRoot = this._applicationRoot(stateSpaceId) +
          `/entities/${encodeURIComponent(encounterId)}`;
        try {
          const knownSummaries = this._entityComponentSummaries.get(encounterId);
          const [entity, summaries] = await Promise.all([
            this._readJson(entityRoot, signal), knownSummaries || this._readAll(entityRoot + '/components', signal)
          ]);
          this._entityComponentSummaries.set(encounterId, summaries);
          const wantedIds = [DND2024_COMPONENTS.encounterOrder, DND2024_COMPONENTS.encounterTurn];
          const wanted = summaries.filter(item => wantedIds.includes(item.qualifiedTypeId));
          const details = await Promise.allSettled(wanted.map(async summary => [
            summary.qualifiedTypeId,
            await this._readJson(entityRoot + `/components/${encodeURIComponent(summary.qualifiedTypeId)}`, signal)
          ]));
          const components = new Map();
          for (let index = 0; index < details.length; index += 1) {
            const result = details[index];
            if (result.status === 'fulfilled') components.set(result.value[0], result.value[1]);
            else components.set(wanted[index].qualifiedTypeId, {});
          }
          const orderDetail = components.get(DND2024_COMPONENTS.encounterOrder);
          const order = this._componentValue(orderDetail);
          const campaignActorIds = new Set(this._entities.map(item => item.entityId));
          const rosterPage = this._validEncounterOrder(order)
            ? await this._loadInventoryPage(stateSpaceId, encounterId, 100, signal) : null;
          if (!this._validEncounterOrder(order) ||
            !this._encounterRosterMatches(order, rosterPage, encounterId) ||
            !order.order.every(item => campaignActorIds.has(item.participantId))) {
            this._encounterStatus = 'unavailable';
            return;
          }
          this._encounterEntity = entity;
          this._encounterComponents = components;
          this._encounterStatus = 'ok';
        } catch (error) {
          if (error.name === 'AbortError') throw error;
          this._encounterStatus = 'unavailable';
        }
      }

      async _findEntitiesWithComponent(stateSpaceId, candidates, componentId, signal) {
        const matches = new Array(candidates.length);
        let nextIndex = 0;
        const worker = async () => {
          while (nextIndex < candidates.length) {
            const index = nextIndex;
            nextIndex += 1;
            const candidate = candidates[index];
            if (!candidate || typeof candidate.entityId !== 'string' || !candidate.entityId.trim()) continue;
            try {
              const summaries = this._entityComponentSummaries.get(candidate.entityId) ||
                await this._readAll(this._applicationRoot(stateSpaceId) +
                  `/entities/${encodeURIComponent(candidate.entityId)}/components`, signal);
              this._entityComponentSummaries.set(candidate.entityId, summaries);
              if (summaries.some(item => item?.qualifiedTypeId === componentId)) matches[index] = candidate;
            } catch (error) {
              if (error.name === 'AbortError') throw error;
            }
          }
        };
        await Promise.all(Array.from({length: Math.min(DND2024_CHARACTER_FILTER_CONCURRENCY, candidates.length)},
          () => worker()));
        return matches.filter(Boolean);
      }

      async _readRelationships(stateSpaceId, fromEntityId, qualifiedKind, signal) {
        const url = new URL(this._applicationRoot(stateSpaceId) + '/relationships', window.location.origin);
        url.searchParams.set('fromEntityId', fromEntityId);
        url.searchParams.set('qualifiedKind', qualifiedKind);
        const values = await this._readAll(url, signal);
        if (!values.every(item => item && item.stateSpaceId === stateSpaceId &&
          item.fromEntityId === fromEntityId && item.qualifiedKind === qualifiedKind &&
          typeof item.toEntityId === 'string' && item.toEntityId.length > 0)) {
          throw new Error('invalid-relationship-list');
        }
        return values;
      }

      async _readRelationshipsForKinds(stateSpaceId, fromEntityId, qualifiedKinds, signal) {
        for (const qualifiedKind of qualifiedKinds) {
          const values = await this._readRelationships(stateSpaceId, fromEntityId, qualifiedKind, signal);
          if (values.length) return values;
        }
        return [];
      }

      async _findDndCharacters(stateSpaceId, candidates, signal) {
        const matches = new Array(candidates.length);
        let nextIndex = 0;
        const worker = async () => {
          while (nextIndex < candidates.length) {
            const index = nextIndex;
            nextIndex += 1;
            if (await this._isDndCharacter(stateSpaceId, candidates[index], signal)) {
              matches[index] = candidates[index];
            }
          }
        };
        const workers = Array.from({length: Math.min(DND2024_CHARACTER_FILTER_CONCURRENCY, candidates.length)},
          () => worker());
        await Promise.all(workers);
        return matches.filter(Boolean);
      }

      _componentValue(detail) {
        if (!detail || typeof detail.valueJson !== 'string') return null;
        try {
          const value = JSON.parse(detail.valueJson);
          return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
        } catch { return null; }
      }

      async _isDndCharacter(stateSpaceId, candidate, signal) {
        if (!candidate || typeof candidate.entityId !== 'string' || !candidate.entityId.trim()) return false;
        const entityRoot = this._applicationRoot(stateSpaceId) +
          `/entities/${encodeURIComponent(candidate.entityId)}`;
        try {
          const summaries = await this._readAll(entityRoot + '/components', signal);
          const available = new Set(summaries.map(item => item?.qualifiedTypeId));
          return DND2024_CHARACTER_COMPONENTS.every(componentId => available.has(componentId));
        } catch (error) {
          if (error.name === 'AbortError') throw error;
          return false;
        }
      }

      async _loadCurrentContext(stateSpaceId, entityId, signal) {
        const entityRoot = this._applicationRoot(stateSpaceId) +
          `/entities/${encodeURIComponent(entityId)}`;
        const wrapper = await this._readJson(entityRoot + '/containment', signal);
        if (!this._closedInventoryValue(wrapper, ['containment'])) {
          throw new Error('invalid-direct-containment-wrapper');
        }
        const containment = wrapper.containment;
        if (containment === null) {
          this._selectedScenePersonId = '';
          return {status: 'missing', location: null, people: [], boundary: null};
        }
        if (!this._isDirectContainment(containment, stateSpaceId, entityId)) {
          throw new Error('invalid-direct-containment');
        }
        if (containment.slot !== 'presence') {
          this._selectedScenePersonId = '';
          return {status: 'missing', location: null, people: [], boundary: null};
        }

        const locationId = containment.containerEntityId;
        const locationRoot = this._applicationRoot(stateSpaceId) +
          `/entities/${encodeURIComponent(locationId)}`;
        const knownSummaries = this._entityComponentSummaries.get(locationId);
        const [locationEntity, summaries] = await Promise.all([
          this._readJson(locationRoot, signal),
          knownSummaries || this._readAll(locationRoot + '/components', signal)
        ]);
        this._entityComponentSummaries.set(locationId, summaries);
        const locationSummaries = summaries.filter(item =>
          item?.qualifiedTypeId === DND2024_WORLD_LOCATION_COMPONENT);
        if (locationSummaries.length !== 1) throw new Error('location-component-unavailable');
        const locationDetail = await this._readJson(locationRoot +
          `/components/${encodeURIComponent(DND2024_WORLD_LOCATION_COMPONENT)}`, signal);
        const locationValue = this._componentValue(locationDetail);
        if (!this._validLocationValue(locationValue)) throw new Error('invalid-location-component');

        const page = await this._loadInventoryPage(
          stateSpaceId, locationId, DND2024_SCENE_MAXIMUM_ENTRIES, signal);
        const candidateRows = [];
        for (const row of page.items) {
          if (!this._isDirectContainment(row, stateSpaceId, row?.containedEntityId) ||
            row.containerEntityId !== locationId) {
            throw new Error('invalid-scene-containment');
          }
          if (row.slot === 'presence' && row.containedEntityId !== entityId) candidateRows.push(row);
        }
        const settledPeople = await Promise.allSettled(candidateRows.map(row =>
          this._loadScenePerson(stateSpaceId, row.containedEntityId, signal)));
        const people = [{entity: this._entity, kind: 'current-character'}];
        let partial = false;
        for (const result of settledPeople) {
          if (result.status === 'fulfilled') {
            if (result.value) people.push(result.value);
          } else {
            if (result.reason?.name === 'AbortError') throw result.reason;
            partial = true;
          }
        }
        if (!people.some(person => person.entity?.entityId === this._selectedScenePersonId)) {
          this._selectedScenePersonId = entityId;
        }
        return {
          status: 'ok',
          containment,
          location: {entity: locationEntity, value: locationValue, detail: locationDetail},
          people,
          boundary: typeof page.nextCursor === 'string' && page.nextCursor.length > 0
            ? 'page-limit' : partial ? 'partial' : null
        };
      }

      async _loadScenePerson(stateSpaceId, entityId, signal) {
        const root = this._applicationRoot(stateSpaceId) + `/entities/${encodeURIComponent(entityId)}`;
        const knownSummaries = this._entityComponentSummaries.get(entityId);
        const [entity, summaries] = await Promise.all([
          this._readJson(root, signal), knownSummaries || this._readAll(root + '/components', signal)
        ]);
        this._entityComponentSummaries.set(entityId, summaries);
        const ids = new Set(summaries.map(item => item?.qualifiedTypeId));
        const campaignActor = this._entities.some(item => item.entityId === entityId);
        const recurringActor = ids.has(DND2024_WORLD_MOTIVE_COMPONENT);
        if (!campaignActor && !recurringActor) return null;
        return {entity, kind: campaignActor ? 'campaign-character' : 'recurring-actor'};
      }

      _isDirectContainment(value, stateSpaceId, entityId) {
        return this._closedInventoryValue(value, [
          'stateSpaceId', 'containedEntityId', 'containerEntityId', 'slot',
          'revision', 'createdAtUtc', 'updatedAtUtc'
        ]) && value.stateSpaceId === stateSpaceId && value.containedEntityId === entityId &&
          typeof value.containedEntityId === 'string' && value.containedEntityId.length >= 1 &&
          value.containedEntityId.length <= 200 &&
          typeof value.containerEntityId === 'string' && value.containerEntityId.length >= 1 &&
          value.containerEntityId.length <= 200 && typeof value.slot === 'string' &&
          value.slot.length >= 1 && value.slot.length <= 100 &&
          Number.isInteger(value.revision) && value.revision >= 1 &&
          typeof value.createdAtUtc === 'string' && value.createdAtUtc.length >= 1 &&
          value.createdAtUtc.length <= 64 && typeof value.updatedAtUtc === 'string' &&
          value.updatedAtUtc.length >= 1 && value.updatedAtUtc.length <= 64;
      }

      _validLocationValue(value) {
        return this._closedInventoryValue(value, ['kind', 'status', 'summary', 'visibility']) &&
          ['region', 'settlement', 'site', 'interior'].includes(value.kind) &&
          value.status === 'active' && typeof value.summary === 'string' &&
          value.summary.length >= 1 && value.summary.length <= 1000 &&
          ['public', 'party', 'gm'].includes(value.visibility);
      }

      async _loadInventory(stateSpaceId, entityId, signal) {
        const context = {remainingEntries: DND2024_INVENTORY_MAXIMUM_ENTRIES,
          seenEntityIds: new Set([entityId])};
        const branch = await this._loadInventoryBranch(stateSpaceId, entityId, 0, context, signal);
        return {status: 'ok', contents: branch.contents, boundary: branch.boundary};
      }

      async _loadInventoryBranch(stateSpaceId, containerEntityId, depth, context, signal) {
        if (depth >= DND2024_INVENTORY_MAXIMUM_DEPTH) {
          return {contents: [], boundary: 'depth-limit'};
        }
        if (context.remainingEntries < 1) {
          return {contents: [], boundary: 'entry-limit'};
        }
        const page = await this._loadInventoryPage(stateSpaceId, containerEntityId,
          Math.min(DND2024_INVENTORY_PAGE_SIZE, context.remainingEntries), signal);
        const contents = [];
        for (const containment of page.items) {
          context.remainingEntries -= 1;
          if (!this._isInventoryContainment(containment, containerEntityId)) {
            contents.push({status: 'unavailable', containment, components: new Map(),
              boundary: 'malformed-containment'});
            continue;
          }
          const entityId = containment.containedEntityId;
          if (context.seenEntityIds.has(entityId)) {
            contents.push({status: 'unavailable', containment, components: new Map(),
              boundary: 'repeated-entity'});
            continue;
          }
          context.seenEntityIds.add(entityId);
          let entry;
          try {
            entry = await this._loadContainedEntity(stateSpaceId, containment, containerEntityId, signal);
          } catch (error) {
            entry = {status: 'unavailable', containment, components: new Map(),
              boundary: error.code || error.message || 'CONTENT_UNAVAILABLE'};
          }
          if (depth + 1 >= DND2024_INVENTORY_MAXIMUM_DEPTH) {
            entry.children = {contents: [], boundary: 'depth-limit'};
          } else {
            try {
              entry.children = await this._loadInventoryBranch(stateSpaceId, entityId, depth + 1,
                context, signal);
            } catch (error) {
              if (error.name === 'AbortError') throw error;
              entry.children = {contents: [], boundary: 'unavailable'};
            }
          }
          contents.push(entry);
          if (context.remainingEntries < 1) break;
        }
        return {contents, boundary: typeof page.nextCursor === 'string' && page.nextCursor.length > 0
          ? 'page-limit' : context.remainingEntries < 1 ? 'entry-limit' : null};
      }

      async _loadInventoryPage(stateSpaceId, containerEntityId, limit, signal) {
        const url = new URL(this._applicationRoot(stateSpaceId) + '/containments', window.location.origin);
        url.searchParams.set('containerEntityId', containerEntityId);
        url.searchParams.set('limit', String(limit));
        const page = await this._readJson(url, signal);
        if (!Array.isArray(page.items) || page.items.length > limit) {
          throw new Error('invalid-containment-list');
        }
        return page;
      }

      _isInventoryContainment(containment, expectedContainerId) {
        if (!containment || typeof containment.containedEntityId !== 'string' ||
          containment.containedEntityId.length < 1 || containment.containedEntityId.length > 200 ||
          typeof containment.containerEntityId !== 'string' ||
          containment.containerEntityId !== expectedContainerId ||
          typeof containment.slot !== 'string' || containment.slot.length > 100 ||
          !Number.isInteger(containment.revision) || containment.revision < 1) {
          return false;
        }
        return true;
      }

      async _loadContainedEntity(stateSpaceId, containment, expectedContainerId, signal) {
        if (!this._isInventoryContainment(containment, expectedContainerId)) {
          throw new Error('invalid-containment');
        }
        const entityId = containment.containedEntityId;
        const entityRoot = this._applicationRoot(stateSpaceId) + `/entities/${encodeURIComponent(entityId)}`;
        const summaries = await this._readAll(entityRoot + '/components', signal);
        const inventoryIds = [DND2024_COMPONENTS.itemInstance, DND2024_COMPONENTS.itemQuantity,
          DND2024_COMPONENTS.equipmentState];
        const wanted = summaries.filter(item => inventoryIds.includes(item.qualifiedTypeId));
        const details = await Promise.allSettled(wanted.map(async summary => [
          summary.qualifiedTypeId,
          await this._readJson(entityRoot + `/components/${encodeURIComponent(summary.qualifiedTypeId)}`, signal)
        ]));
        const components = new Map();
        for (let index = 0; index < details.length; index += 1) {
          const result = details[index];
          if (result.status === 'fulfilled') components.set(result.value[0], result.value[1]);
          else components.set(wanted[index].qualifiedTypeId, {});
        }
        let definition = {status: 'missing'};
        const instance = this._inventoryComponent({components}, DND2024_COMPONENTS.itemInstance);
        if (instance.status === 'unavailable') definition = {status: 'unavailable'};
        if (instance.status === 'ok') {
          const definitionId = instance.value.definitionId;
          if (typeof definitionId !== 'string' || definitionId.length < 4 || definitionId.length > 200) {
            definition = {status: 'unavailable'};
          } else {
            try { definition = await this._loadItemDefinition(definitionId, signal); }
            catch { definition = {status: 'unavailable'}; }
          }
        }
        const entity = this._entities.find(value => value.entityId === entityId);
        return {status: 'ok', containment, components, definition, name: entity?.name || entityId};
      }

      async _loadItemDefinition(definitionId, signal) {
        const prefix = `${this.applicationId}.`;
        const qualifiedId = definitionId.startsWith(prefix) ? definitionId : prefix + definitionId;
        const url = new URL(`/api/applications/${encodeURIComponent(this.applicationId)}/catalog/records/${encodeURIComponent(qualifiedId)}`,
          window.location.origin);
        url.searchParams.set('collection', this.applicationId);
        const record = await this._readJson(url, signal);
        if (!record || !record.summary || record.summary.kind !== 'entity' ||
          record.summary.qualifiedId !== qualifiedId || record.summary.collection !== this.applicationId ||
          typeof record.contentJson !== 'string') throw new Error('invalid-item-definition-record');
        const content = JSON.parse(record.contentJson);
        const components = content && typeof content === 'object' && !Array.isArray(content)
          ? content.components : null;
        const value = components && typeof components === 'object' && !Array.isArray(components)
          ? components[DND2024_COMPONENTS.itemDefinition] : null;
        if (content.id !== definitionId || typeof content.name !== 'string' || !content.name.trim() ||
          !value || typeof value !== 'object' || Array.isArray(value)) {
          throw new Error('invalid-item-definition-content');
        }
        const activity = components[DND2024_COMPONENTS.itemActivity];
        const activityState = activity === undefined ? {status: 'missing'} :
          activity && typeof activity === 'object' && !Array.isArray(activity)
            ? {status: 'ok', value: activity} : {status: 'unavailable'};
        return {status: 'ok', name: content.name, value, activity: activityState,
          summary: record.summary};
      }

      async _readAll(path, signal) {
        const values = [];
        const cursors = new Set();
        let cursor = null;
        for (let page = 0; page < DND2024_MAXIMUM_PAGES; page += 1) {
          const url = new URL(path, window.location.origin);
          url.searchParams.set('limit', '100');
          if (cursor) url.searchParams.set('cursor', cursor);
          const result = await this._readJson(url, signal);
          if (!Array.isArray(result.items)) throw new Error('invalid-list');
          values.push(...result.items);
          if (values.length > DND2024_MAXIMUM_RECORDS) throw new Error('record-limit');
          cursor = typeof result.nextCursor === 'string' && result.nextCursor ? result.nextCursor : null;
          if (!cursor) return values;
          if (cursors.has(cursor)) throw new Error('cursor-loop');
          cursors.add(cursor);
        }
        throw new Error('page-limit');
      }

      async _readJson(input, signal) {
        const response = await fetch(input, {headers: {accept: 'application/json'}, signal});
        if (!response.ok) {
          let code = `HTTP_${response.status}`;
          try { code = (await response.json()).error || code; } catch { }
          const error = new Error(code);
          error.code = code;
          throw error;
        }
        return await response.json();
      }

      _fillSelect(select, items, valueKey, labelKey, requested, emptyLabel) {
        select.replaceChildren();
        if (!items.length) {
          const option = document.createElement('option');
          option.textContent = emptyLabel;
          option.value = '';
          select.append(option);
          select.disabled = true;
          return;
        }
        select.disabled = false;
        for (const item of items) {
          const option = document.createElement('option');
          option.value = String(item[valueKey] ?? '');
          option.textContent = String(item[labelKey] || item[valueKey] || 'Unnamed');
          select.append(option);
        }
        if (requested && items.some(item => item[valueKey] === requested)) select.value = requested;
      }

      _component(id) {
        const detail = this._components.get(id);
        if (!detail) return {status: 'missing'};
        if (typeof detail.valueJson !== 'string') return {status: 'unavailable'};
        try {
          const value = JSON.parse(detail.valueJson);
          return value && typeof value === 'object' && !Array.isArray(value)
            ? {status: 'ok', value, revision: detail.revision}
            : {status: 'unavailable'};
        } catch { return {status: 'unavailable'}; }
      }

      _encounterComponent(id) {
        if (this._encounterStatus === 'unavailable') return {status: 'unavailable'};
        const detail = this._encounterComponents.get(id);
        if (!detail) return {status: 'missing'};
        if (typeof detail.valueJson !== 'string') return {status: 'unavailable'};
        try {
          const value = JSON.parse(detail.valueJson);
          return value && typeof value === 'object' && !Array.isArray(value)
            ? {status: 'ok', value, revision: detail.revision}
            : {status: 'unavailable'};
        } catch { return {status: 'unavailable'}; }
      }

      _renderState() {
        this._name.textContent = this._entity?.name || this._entity?.entityId || 'Unnamed entity';
        this._scope.textContent = `${this.applicationId} / ${this.selectedStateSpaceId} / ${this.selectedEntityId} · entity r${this._entity?.revision ?? '—'}`;
        const level = this._component(DND2024_COMPONENTS.level);
        this._level.textContent = level.status === 'ok' && Number.isInteger(level.value.level)
          ? `Level ${level.value.level}` : 'Level —';
        this._renderCampaign();
        this._renderVitals();
        this._renderDossier();
        this._renderAbilities();
        this._renderInventory();
        this._renderTurnBudget();
        this._renderActions();
        this._renderConditions();
        this._renderSpeed();
        this._renderProficiencies();
        this._renderMitigation();
        this._renderEncounter();
        this._renderLocation();
        this._renderScenePeople();
        this._renderKnowledge();
      }

      _renderDossier() {
        const body = this._dossier.body;
        body.replaceChildren();
        body.className = 'panel-body dossier';
        const legacy = this._component(DND2024_LEGACY_CHARACTER_COMPONENT);
        if (legacy.status === 'ok' && Array.isArray(legacy.value.entries)) {
          body.append(this._element('div', 'inventory-boundary',
            'This character is recorded in the legacy campaign format. Current D&D action controls stay unavailable until it is migrated.'));
          const copy = this._element('div', 'dossier-copy');
          for (const entry of legacy.value.entries.slice(0, 24)) {
            if (!entry || typeof entry.label !== 'string' || typeof entry.details !== 'string') continue;
            const card = this._element('article', 'dossier-entry');
            card.append(this._element('strong', '', entry.label), this._element('p', '', entry.details));
            copy.append(card);
          }
          body.append(copy.childElementCount ? copy : this._empty('Legacy character details are unavailable.'));
          return;
        }
        const profile = this._component(DND2024_COMPONENTS.characterProfile);
        const size = this._component(DND2024_COMPONENTS.creatureSize);
        const experience = this._component(DND2024_COMPONENTS.characterExperience);
        const level = this._component(DND2024_COMPONENTS.level);

        const profileValid = profile.status === 'ok' &&
          ['pronouns', 'appearance', 'biography'].every(key =>
            profile.value[key] === undefined ||
            (typeof profile.value[key] === 'string' && profile.value[key].length > 0));
        if (profile.status === 'unavailable' || (profile.status === 'ok' && !profileValid)) {
          body.append(this._empty('Character profile is unavailable.'));
        }

        const stats = this._element('div', 'dossier-stats');
        const exactLevel = level.status === 'ok' && Number.isInteger(level.value.level) &&
          level.value.level >= 1 && level.value.level <= 20
          ? String(level.value.level) : 'Unknown';
        const exactSize = size.status === 'ok' &&
          ['tiny', 'small', 'medium', 'large', 'huge', 'gargantuan'].includes(size.value.size)
          ? this._label(size.value.size) : 'Unknown';
        const exactExperience = experience.status === 'ok' && Number.isSafeInteger(experience.value.total) &&
          experience.value.total >= 0
          ? String(experience.value.total) : 'Unknown';
        for (const [label, value] of [
          ['Level', exactLevel], ['Size', exactSize], ['Experience', exactExperience],
          ['Entity revision', String(this._entity?.revision ?? 'Unknown')]
        ]) {
          const card = this._element('div', 'dossier-stat');
          card.append(this._element('span', '', label), this._element('strong', '', value));
          stats.append(card);
        }
        body.append(stats);

        const copy = this._element('div', 'dossier-copy');
        const profileValue = profileValid ? profile.value : {};
        const profileFallback = profile.status === 'missing' ? 'Not recorded' : 'Unavailable';
        for (const [label, key] of [
          ['Pronouns', 'pronouns'], ['Appearance', 'appearance'], ['Biography', 'biography']
        ]) {
          const entry = this._element('article', 'dossier-entry');
          entry.append(this._element('strong', '', label),
            this._element('p', '', profileValue[key] || profileFallback));
          copy.append(entry);
        }
        body.append(copy);
      }

      _renderEncounter() {
        const body = this._encounter.body;
        body.replaceChildren();
        body.className = 'panel-body';
        if (!this.selectedEncounterId) {
          body.append(this._empty('No recorded order-bearing encounter is available for this campaign.'));
          return;
        }
        if (this._encounterStatus === 'unavailable') {
          body.append(this._empty('The selected encounter is unavailable or no longer belongs to this campaign.'));
          return;
        }
        const order = this._encounterComponent(DND2024_COMPONENTS.encounterOrder);
        const turn = this._encounterComponent(DND2024_COMPONENTS.encounterTurn);
        if (order.status === 'missing') {
          body.append(this._empty('No Initiative snapshot is recorded on this entity.'));
          return;
        }
        if (order.status !== 'ok' || !this._validEncounterOrder(order.value)) {
          body.append(this._empty('Initiative order is unavailable.'));
          return;
        }
        const names = new Map(this._entities.map(entity => [entity.entityId, entity.name || entity.entityId]));
        const tracker = document.createElement('dnd2024-encounter-tracker');
        tracker.setAttribute('application-id', this.applicationId);
        tracker.setAttribute('state-space-id', this.selectedStateSpaceId);
        tracker.setAttribute('encounter-entity-id', this.selectedEncounterId);
        tracker.context = {order: order.value, turnStatus: turn.status,
          turn: turn.status === 'ok' ? turn.value : null, participantNames: names};
        tracker.actionsAvailable = this._actionsAvailable;
        tracker.addEventListener('dnd2024-encounter-refresh', () => this._loadEntity());
        body.append(tracker);
      }

      _renderInventory() {
        this._inventoryActionGeneration += 1;
        const body = this._inventoryPanel.body;
        body.replaceChildren();
        body.className = 'panel-body inventory-stack';
        if (this._inventory.status === 'unavailable') {
          body.append(this._empty('Inventory contents are unavailable.'));
          return;
        }
        const contents = Array.isArray(this._inventory.contents) ? this._inventory.contents : [];
        if (!contents.length) {
          body.append(this._empty('No carried contents are recorded for this entity.'));
          this._appendInventoryBoundary(body, this._inventory.boundary);
          return;
        }

        const items = [];
        const other = [];
        for (const entry of contents) {
          const instance = this._inventoryComponent(entry, DND2024_COMPONENTS.itemInstance);
          if (instance.status === 'ok' || instance.status === 'unavailable') items.push([entry, instance]);
          else other.push(entry);
        }

        if (items.length) {
          const group = this._element('section');
          group.append(this._element('p', 'group-title', 'Carried items'));
          const grid = this._element('div', 'inventory-grid');
          for (const [entry, instance] of items) grid.append(this._inventoryNode(entry, instance, 1));
          group.append(grid);
          body.append(group);
        }
        if (other.length) {
          const group = this._element('section');
          group.append(this._element('p', 'group-title', 'Other contents'));
          const stack = this._element('div', 'stack');
          for (const entry of other) stack.append(this._inventoryNode(entry, null, 1));
          group.append(stack);
          body.append(group);
        }
        this._appendInventoryBoundary(body, this._inventory.boundary);
        body.append(this._element('div', 'inventory-boundary',
          `This view shows at most ${DND2024_INVENTORY_MAXIMUM_ENTRIES} contents across ${DND2024_INVENTORY_MAXIMUM_DEPTH} levels. Ordinary actions appear only where their complete required state is visible.`));
      }

      _inventoryNode(entry, instance, depth) {
        const node = this._element('div', 'inventory-node');
        node.dataset.depth = String(depth);
        const containment = entry?.containment || {};
        if (instance) {
          node.append(this._inventoryCard(entry, instance, depth));
        } else {
          const card = this._element('div', 'contained-card');
          const identity = this._element('span', '', entry?.name || containment.containedEntityId || 'Unavailable content');
          identity.append(this._element('small', '', containment.containedEntityId || 'Unknown entity'));
          card.append(identity, this._element('span', 'item-tag', containment.slot || 'Unslotted'));
          node.append(card);
        }
        const children = entry?.children;
        if (children && (Array.isArray(children.contents) && children.contents.length || children.boundary)) {
          const nested = this._element('section', 'inventory-children');
          nested.append(this._element('p', 'group-title', `Inside ${entry.name || containment.containedEntityId || 'container'}`),
            this._element('span', 'inventory-depth', `Depth ${depth + 1}`));
          for (const child of Array.isArray(children.contents) ? children.contents : []) {
            const childInstance = this._inventoryComponent(child, DND2024_COMPONENTS.itemInstance);
            nested.append(this._inventoryNode(child,
              childInstance.status === 'ok' || childInstance.status === 'unavailable' ? childInstance : null,
              depth + 1));
          }
          this._appendInventoryBoundary(nested, children.boundary);
          node.append(nested);
        }
        this._appendInventoryBoundary(node, entry?.boundary);
        return node;
      }

      _appendInventoryBoundary(parent, boundary) {
        const messages = {
          'page-limit': 'More direct contents are recorded here. This view shows the first 24.',
          'depth-limit': 'Further contents are not checked beyond depth 4.',
          'entry-limit': 'The inventory tree stops after 96 visible contents.',
          'repeated-entity': 'A repeated containment identity was skipped.',
          'malformed-containment': 'One containment record is unavailable.',
          unavailable: 'Nested contents are unavailable.'
        };
        if (boundary && messages[boundary]) {
          parent.append(this._element('div', 'inventory-boundary', messages[boundary]));
        }
      }

      _inventoryCard(entry, instance, depth) {
        const containment = entry.containment || {};
        const definition = entry.definition || {status: 'missing'};
        const card = this._element('article', 'item-card');
        const head = this._element('div', 'item-head');
        const rune = this._element('span', 'item-rune', '✦');
        rune.setAttribute('aria-hidden', 'true');
        const identity = this._element('div');
        identity.append(this._element('span', 'item-name', definition.status === 'ok'
          ? definition.name : entry.name || containment.containedEntityId || 'Unavailable item'),
          this._element('span', 'item-id', entry.name || containment.containedEntityId || 'Unknown entity'));
        const quantity = this._inventoryComponent(entry, DND2024_COMPONENTS.itemQuantity);
        const countValid = quantity.status === 'ok' && Number.isSafeInteger(quantity.value.count) &&
          quantity.value.count > 0 && typeof quantity.value.stackKey === 'string';
        const count = quantity.status === 'missing' ? 'Individual' : countValid ? `×${quantity.value.count}` : 'Quantity unavailable';
        head.append(rune, identity, this._element('span', 'item-count', count));
        card.append(head);

        const meta = this._element('div', 'item-meta');
        const definitionValid = instance.status === 'ok' && typeof instance.value.definitionId === 'string' &&
          instance.value.definitionId.length >= 4 && instance.value.definitionId.length <= 200;
        meta.append(this._element('span', `item-tag${definitionValid ? '' : ' warn'}`,
          definitionValid ? instance.value.definitionId : 'Item identity unavailable'));
        if (definition.status === 'ok' && typeof definition.value.kind === 'string') {
          meta.append(this._element('span', 'item-tag', this._label(definition.value.kind)));
        }
        if (definition.status === 'ok' && typeof definition.value.stackPolicy === 'string') {
          meta.append(this._element('span', 'item-tag', `${this._label(definition.value.stackPolicy)} stack`));
        }
        if (definition.status === 'unavailable') {
          meta.append(this._element('span', 'item-tag warn', 'Definition unavailable'));
        }
        meta.append(this._element('span', 'item-tag', containment.slot || 'Unslotted'));
        const equipment = this._inventoryComponent(entry, DND2024_COMPONENTS.equipmentState);
        const equipmentValid = equipment.status === 'ok' &&
          ['held', 'worn', 'unequipped'].includes(equipment.value.state);
        if (equipment.status !== 'missing') {
          meta.append(this._element('span', `item-tag${equipmentValid && equipment.value.state !== 'unequipped' ? ' equipped' : equipmentValid ? '' : ' warn'}`,
            equipmentValid ? this._label(equipment.value.state) : 'Equipment unavailable'));
        }
        meta.append(this._element('span', 'item-tag', Number.isInteger(containment.revision)
          ? `Custody r${containment.revision}` : 'Custody unavailable'));
        card.append(meta);
        if (definition.status === 'ok') {
          const facts = [];
          const mass = this._fraction(definition.value.massPounds);
          if (mass) facts.push(['Weight', `${mass} lb.`]);
          const capacity = definition.value.capacity;
          if (capacity && typeof capacity === 'object' && !Array.isArray(capacity)) {
            const weight = this._fraction(capacity.weightPounds);
            const volume = this._fraction(capacity.volumeCubicFeet);
            if (weight) facts.push(['Capacity', `${weight} lb.`]);
            if (volume) facts.push(['Volume', `${volume} cu. ft.`]);
            if (Number.isSafeInteger(capacity.itemCount) && capacity.itemCount > 0) {
              facts.push(['Item capacity', String(capacity.itemCount)]);
            }
          }
          if (Array.isArray(definition.value.equipmentModes) &&
            definition.value.equipmentModes.every(value => typeof value === 'string')) {
            facts.push(['Equipment modes', definition.value.equipmentModes.map(value => this._label(value)).join(', ')]);
          }
          const currency = definition.value.currency;
          if (currency && typeof currency === 'object' && !Array.isArray(currency) &&
            typeof currency.denomination === 'string') facts.push(['Denomination', currency.denomination.toUpperCase()]);
          if (facts.length) {
            const factGrid = this._element('div', 'item-facts');
            for (const [label, value] of facts) {
              const fact = this._element('div', 'item-fact');
              fact.append(this._element('small', '', label), this._element('span', '', value));
              factGrid.append(fact);
            }
            card.append(factGrid);
          }
          const source = definition.value.sourceRef;
          if (source && typeof source === 'object' && !Array.isArray(source) &&
            typeof source.sourceId === 'string' && typeof source.locator === 'string') {
            card.append(this._element('div', 'item-source', `${source.sourceId} · ${source.locator}`));
          }
        }
        this._appendEquipmentActions(card, entry, instance, definition, quantity, equipment,
          equipmentValid, depth);
        this._appendOrdinaryInventoryActions(card, entry, instance, definition, quantity,
          equipment, equipmentValid);
        return card;
      }

      _appendEquipmentActions(card, entry, instance, definition, quantity, equipment,
        equipmentValid, depth) {
        const containment = entry?.containment || {};
        const itemId = containment.containedEntityId;
        if (!this._actionsAvailable || depth !== 1 ||
          containment.containerEntityId !== this.selectedEntityId ||
          typeof itemId !== 'string' || itemId.length < 1 || itemId.length > 200 ||
          instance.status !== 'ok' || typeof instance.value.definitionId !== 'string' ||
          quantity.status !== 'missing' || definition.status !== 'ok' ||
          (equipment.status !== 'missing' && !equipmentValid)) return;

        const declaredModes = definition.value.equipmentModes;
        const modes = Array.isArray(declaredModes) && declaredModes.length >= 1 && declaredModes.length <= 2 &&
          declaredModes.every(mode => mode === 'held' || mode === 'worn') &&
          new Set(declaredModes).size === declaredModes.length ? declaredModes : null;
        if (!modes) return;

        const currentState = equipment.status === 'ok' ? equipment.value.state : null;
        const actions = this._element('section', 'item-actions');
        actions.setAttribute('aria-label', `Equipment actions for ${entry.name || itemId}`);
        actions.addEventListener('application-receipt', event => {
          if (event.detail?.phase === 'complete') this._loadEntity();
        });
        const heading = this._element('div', 'item-actions-head');
        heading.append(this._element('strong', '', 'Equipment'),
          this._element('span', '', currentState === 'held' || currentState === 'worn'
            ? `Currently ${this._label(currentState)}` : 'Choose a permitted mode'));
        actions.append(heading);
        const slot = this._element('div', 'item-action-slot');

        if (currentState === 'held' || currentState === 'worn') {
          actions.append(slot);
          this._mountInventoryAction(slot, this._inventoryActionGeneration, {
            mechanicId: 'mechanic.dnd2024.item.unequip',
            label: 'Prepare unequip', itemId, input: {}
          });
          card.append(actions);
          return;
        }

        const remembered = this._equipmentModeByItem.get(itemId);
        const selectedMode = modes.includes(remembered) ? remembered : modes[0];
        this._equipmentModeByItem.set(itemId, selectedMode);
        const choices = this._element('div', 'item-mode-grid');
        for (const mode of modes) {
          const button = this._element('button', 'item-mode', mode === 'held' ? 'Hold' : 'Wear');
          button.type = 'button';
          button.setAttribute('aria-pressed', String(mode === selectedMode));
          button.addEventListener('click', () => {
            this._equipmentModeByItem.set(itemId, mode);
            this._renderInventory();
          });
          choices.append(button);
        }
        actions.append(choices, slot);
        this._mountInventoryAction(slot, this._inventoryActionGeneration, {
          mechanicId: 'mechanic.dnd2024.item.equip',
          label: selectedMode === 'held' ? 'Prepare hold' : 'Prepare wear',
          itemId, input: {state: selectedMode}
        });
        card.append(actions);
      }

      async _mountInventoryAction(slot, generation, configuration) {
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._inventoryActionGeneration || !slot.isConnected) return;
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.selectedStateSpaceId);
        control.setAttribute('mechanic-id', configuration.mechanicId);
        control.textContent = configuration.label;
        control.roleEntityIds = configuration.roles ||
          {item: configuration.itemId, holder: this.selectedEntityId};
        control.input = configuration.input;
        slot.replaceChildren(control);
      }

      _appendOrdinaryInventoryActions(card, entry, instance, definition, quantity,
        equipment, equipmentValid) {
        if (!this._actionsAvailable || instance.status !== 'ok' || definition.status !== 'ok') return;
        const containment = entry?.containment || {};
        const itemId = containment.containedEntityId;
        const sourceId = containment.containerEntityId;
        if (typeof itemId !== 'string' || itemId.length < 1 || itemId.length > 200 ||
          typeof sourceId !== 'string' || sourceId.length < 1 || sourceId.length > 200 ||
          typeof instance.value.definitionId !== 'string' ||
          (equipment.status !== 'missing' && !equipmentValid)) return;

        const currentEquipment = equipment.status === 'ok' ? equipment.value.state : null;
        if (currentEquipment !== 'held' && currentEquipment !== 'worn') {
          this._appendTransferAction(card, entry, itemId, sourceId);
        }
        const stack = this._validInventoryStack(entry);
        if (stack) this._appendStackActions(card, entry, stack);
      }

      _appendTransferAction(card, entry, itemId, sourceId) {
        const destinations = this._inventoryTransferDestinations(entry, sourceId);
        if (!destinations.length) return;
        let state = this._inventoryTransferByItem.get(itemId);
        if (!state || !destinations.some(value => value.id === state.destinationId)) {
          state = {destinationId: destinations[0].id, slot: destinations[0].kind === 'actor' ? 'carried' : 'inside'};
          this._inventoryTransferByItem.set(itemId, state);
        }
        const selected = destinations.find(value => value.id === state.destinationId) || destinations[0];
        const details = this._element('details', 'item-operation');
        details.open = true;
        const summary = this._element('summary', '', 'Move or give');
        const fields = this._element('div', 'item-operation-grid');
        const destinationLabel = this._element('label');
        destinationLabel.append(this._element('span', '', 'Destination'));
        const destination = document.createElement('select');
        destination.setAttribute('aria-label', 'Transfer destination');
        for (const optionValue of destinations) {
          const option = document.createElement('option');
          option.value = optionValue.id; option.textContent = optionValue.label;
          destination.append(option);
        }
        destination.value = selected.id;
        destination.addEventListener('change', () => {
          const next = destinations.find(value => value.id === destination.value);
          if (!next) return;
          this._inventoryTransferByItem.set(itemId, {
            destinationId: next.id, slot: next.kind === 'actor' ? 'carried' : 'inside'
          });
          this._renderInventory();
        });
        destinationLabel.append(destination);
        const slotLabel = this._element('label');
        slotLabel.append(this._element('span', '', 'Placement label'));
        const slotInput = document.createElement('input');
        slotInput.type = 'text'; slotInput.maxLength = 100; slotInput.value = state.slot;
        slotInput.setAttribute('aria-label', 'Transfer placement label');
        slotInput.addEventListener('change', () => {
          const slot = slotInput.value.trim();
          if (!slot) return;
          this._inventoryTransferByItem.set(itemId, {destinationId: selected.id, slot});
          this._renderInventory();
        });
        slotLabel.append(slotInput);
        fields.append(destinationLabel, slotLabel);
        const actionSlot = this._element('div', 'item-action-slot');
        details.addEventListener('application-receipt', event => {
          if (event.detail?.phase !== 'complete') return;
          this._inventoryTransferByItem.delete(itemId);
          this._loadEntity();
        });
        details.append(summary, fields,
          this._element('p', 'item-operation-note', 'The server checks ordinary admission, capacity, and cycles.'),
          actionSlot);
        this._mountInventoryAction(actionSlot, this._inventoryActionGeneration, {
          mechanicId: 'mechanic.dnd2024.item.transfer', label: 'Prepare transfer',
          roles: {item: itemId, source: sourceId, destination: selected.id}, input: {slot: state.slot}
        });
        card.append(details);
      }

      _appendStackActions(card, entry, stack) {
        const itemId = entry.containment.containedEntityId;
        let state = this._inventoryStackByItem.get(itemId);
        if (!state) {
          state = {consumeCount: 1, splitCount: 1,
            splitName: `${String(entry.definition.name || entry.name || 'Stack').slice(0, 390)} (split)`,
            splitItemId: this._newInventoryEntityId('split'), mergeTargetId: ''};
          this._inventoryStackByItem.set(itemId, state);
        }
        state.consumeCount = Math.min(stack.count, Math.max(1, state.consumeCount));
        state.splitCount = Math.min(Math.max(1, stack.count - 1), Math.max(1, state.splitCount));
        const mergeTargets = this._inventoryEntries().map(value => ({entry: value, stack: this._validInventoryStack(value)}))
          .filter(value => value.stack && value.entry.containment.containedEntityId !== itemId &&
            value.entry.containment.containerEntityId === entry.containment.containerEntityId &&
            value.stack.definitionId === stack.definitionId);
        if (!mergeTargets.some(value => value.entry.containment.containedEntityId === state.mergeTargetId)) {
          state.mergeTargetId = mergeTargets[0]?.entry.containment.containedEntityId || '';
        }

        const details = this._element('details', 'item-operation');
        details.open = true;
        details.append(this._element('summary', '', 'Stack actions'));
        details.addEventListener('application-receipt', event => {
          if (event.detail?.phase !== 'complete') return;
          this._inventoryStackByItem.delete(itemId);
          this._clearInventoryActivityIds(itemId);
          this._loadEntity();
        });

        const consume = this._element('section', 'item-use-list');
        consume.append(this._element('p', 'item-operation-note', 'Consume quantity'));
        consume.append(this._inventoryCountStepper('Consume count', state.consumeCount, 1, stack.count,
          value => { state.consumeCount = value; this._renderInventory(); }));
        if (state.consumeCount === stack.count) {
          consume.append(this._element('p', 'item-operation-note warn',
            'Consuming the full displayed count may remove this stack.'));
        }
        const consumeSlot = this._element('div', 'item-action-slot');
        consume.append(consumeSlot);
        this._mountInventoryAction(consumeSlot, this._inventoryActionGeneration, {
          mechanicId: 'mechanic.dnd2024.item-stack.consume', label: `Prepare consume ${state.consumeCount}`,
          roles: {item: itemId, definition: stack.definitionId}, input: {count: state.consumeCount}
        });
        details.append(consume);

        if (stack.count > 1) {
          const split = this._element('section', 'item-use-list');
          split.append(this._element('p', 'item-operation-note', 'Split into a new stack'));
          split.append(this._inventoryCountStepper('Split count', state.splitCount, 1, stack.count - 1,
            value => { state.splitCount = value; this._renderInventory(); }));
          const nameLabel = this._element('label');
          nameLabel.append(this._element('span', '', 'New stack name'));
          const name = document.createElement('input');
          name.type = 'text'; name.maxLength = 400; name.value = state.splitName;
          name.setAttribute('aria-label', 'New split stack name');
          name.addEventListener('change', () => {
            const value = name.value.trim(); if (!value) return;
            state.splitName = value; this._renderInventory();
          });
          nameLabel.append(name); split.append(nameLabel);
          const splitSlot = this._element('div', 'item-action-slot'); split.append(splitSlot);
          this._mountInventoryAction(splitSlot, this._inventoryActionGeneration, {
            mechanicId: 'mechanic.dnd2024.item-stack.split', label: `Prepare split ${state.splitCount}`,
            roles: {source: itemId, definition: stack.definitionId},
            input: {count: state.splitCount, itemId: state.splitItemId, name: state.splitName}
          });
          details.append(split);
        }

        if (mergeTargets.length) {
          const merge = this._element('section', 'item-use-list');
          const targetLabel = this._element('label'); targetLabel.append(this._element('span', '', 'Merge this stack into'));
          const target = document.createElement('select'); target.setAttribute('aria-label', 'Merge target stack');
          for (const candidate of mergeTargets) {
            const id = candidate.entry.containment.containedEntityId;
            const option = document.createElement('option'); option.value = id;
            option.textContent = candidate.entry.definition.name || candidate.entry.name || id;
            target.append(option);
          }
          target.value = state.mergeTargetId;
          target.addEventListener('change', () => { state.mergeTargetId = target.value; this._renderInventory(); });
          targetLabel.append(target); merge.append(targetLabel,
            this._element('p', 'item-operation-note warn', 'The current stack is the merge source and may be removed.'));
          const mergeSlot = this._element('div', 'item-action-slot'); merge.append(mergeSlot);
          this._mountInventoryAction(mergeSlot, this._inventoryActionGeneration, {
            mechanicId: 'mechanic.dnd2024.item-stack.merge', label: 'Prepare merge',
            roles: {source: itemId, target: state.mergeTargetId, definition: stack.definitionId}, input: {}
          });
          details.append(merge);
        }

        this._appendItemActivities(details, entry, stack);
        card.append(details);
      }

      _appendItemActivities(parent, entry, stack) {
        const activityState = entry.definition.activity || {status: 'missing'};
        if (activityState.status !== 'ok' || !Array.isArray(activityState.value.activities)) return;
        const activities = activityState.value.activities.filter(value => this._validItemActivity(value, stack.count));
        if (!activities.length || activities.length > 12) return;
        const itemId = entry.containment.containedEntityId;
        const section = this._element('section', 'item-use-list');
        section.append(this._element('p', 'item-operation-note', 'Use published item activity'));
        for (const activity of activities) {
          const key = `${itemId}\n${activity.id}`;
          let grantItemId = this._inventoryActivityByItem.get(key);
          if (!grantItemId) {
            grantItemId = this._newInventoryEntityId('grant');
            this._inventoryActivityByItem.set(key, grantItemId);
          }
          section.append(this._element('p', 'item-operation-note',
            `${this._label(activity.id)} · consume ${activity.consumeQuantity} · create ${activity.grant.name}`));
          const slot = this._element('div', 'item-action-slot'); section.append(slot);
          this._mountInventoryAction(slot, this._inventoryActionGeneration, {
            mechanicId: 'mechanic.dnd2024.item-activity.use', label: `Prepare ${this._label(activity.id)}`,
            roles: {item: itemId, definition: stack.definitionId,
              grantDefinition: activity.grant.definitionId},
            input: {activityId: activity.id, grantItemId}
          });
        }
        parent.append(section);
      }

      _validItemActivity(value, availableCount) {
        if (!this._closedInventoryValue(value, ['id', 'kind', 'consumeQuantity', 'grant']) ||
          typeof value.id !== 'string' || value.id.length < 1 || value.id.length > 64 ||
          value.kind !== 'consume-and-grant-item' || !Number.isSafeInteger(value.consumeQuantity) ||
          value.consumeQuantity < 1 || value.consumeQuantity > availableCount ||
          !this._closedInventoryValue(value.grant, ['definitionId', 'name', 'slot']) ||
          typeof value.grant.definitionId !== 'string' || value.grant.definitionId.length < 4 ||
          value.grant.definitionId.length > 200 || typeof value.grant.name !== 'string' ||
          value.grant.name.trim() !== value.grant.name || value.grant.name.length < 1 ||
          value.grant.name.length > 160 || typeof value.grant.slot !== 'string' ||
          value.grant.slot.trim() !== value.grant.slot || value.grant.slot.length < 1 ||
          value.grant.slot.length > 80) return false;
        return true;
      }

      _closedInventoryValue(value, keys) {
        if (!value || typeof value !== 'object' || Array.isArray(value) ||
          Object.keys(value).length !== keys.length) return false;
        return keys.every(key => Object.prototype.hasOwnProperty.call(value, key));
      }

      _clearInventoryActivityIds(itemId) {
        const prefix = `${itemId}\n`;
        for (const key of this._inventoryActivityByItem.keys()) {
          if (key.startsWith(prefix)) this._inventoryActivityByItem.delete(key);
        }
      }

      _validInventoryStack(entry) {
        const instance = this._inventoryComponent(entry, DND2024_COMPONENTS.itemInstance);
        const quantity = this._inventoryComponent(entry, DND2024_COMPONENTS.itemQuantity);
        const definition = entry?.definition;
        const children = entry?.children;
        if (instance.status !== 'ok' || quantity.status !== 'ok' || definition?.status !== 'ok' ||
          definition.value.stackPolicy !== 'fungible' ||
          typeof instance.value.definitionId !== 'string' ||
          quantity.value.stackKey !== instance.value.definitionId ||
          !Number.isSafeInteger(quantity.value.count) || quantity.value.count < 1 ||
          !children || !Array.isArray(children.contents) || children.contents.length || children.boundary) return null;
        return {count: quantity.value.count, definitionId: instance.value.definitionId};
      }

      _inventoryEntries() {
        const result = [];
        const visit = entries => {
          for (const entry of Array.isArray(entries) ? entries : []) {
            result.push(entry);
            visit(entry?.children?.contents);
          }
        };
        visit(this._inventory.contents);
        return result;
      }

      _inventoryTransferDestinations(movingEntry, sourceId) {
        const itemId = movingEntry.containment.containedEntityId;
        const excluded = new Set([itemId, sourceId]);
        const visit = entries => {
          for (const entry of Array.isArray(entries) ? entries : []) {
            excluded.add(entry?.containment?.containedEntityId);
            visit(entry?.children?.contents);
          }
        };
        visit(movingEntry?.children?.contents);
        const values = [];
        const seen = new Set();
        for (const actor of this._entities) {
          if (!actor || typeof actor.entityId !== 'string' || excluded.has(actor.entityId) || seen.has(actor.entityId)) continue;
          seen.add(actor.entityId);
          values.push({id: actor.entityId, label: actor.name || actor.entityId, kind: 'actor'});
        }
        for (const entry of this._inventoryEntries()) {
          const id = entry?.containment?.containedEntityId;
          const capacity = entry?.definition?.status === 'ok' ? entry.definition.value.capacity : null;
          if (typeof id !== 'string' || excluded.has(id) || seen.has(id) ||
            !capacity || typeof capacity !== 'object' || Array.isArray(capacity)) continue;
          seen.add(id);
          values.push({id, label: entry.definition.name || entry.name || id, kind: 'container'});
        }
        return values;
      }

      _inventoryCountStepper(label, value, minimum, maximum, onChange) {
        const stepper = this._element('div', 'item-stack-stepper');
        const lower = this._element('button', '', '−'); lower.type = 'button'; lower.disabled = value <= minimum;
        lower.setAttribute('aria-label', `Decrease ${label.toLowerCase()}`);
        lower.addEventListener('click', () => onChange(Math.max(minimum, value - 1)));
        const display = this._element('strong', '', String(value)); display.setAttribute('aria-label', `${label}: ${value}`);
        const higher = this._element('button', '', '+'); higher.type = 'button'; higher.disabled = value >= maximum;
        higher.setAttribute('aria-label', `Increase ${label.toLowerCase()}`);
        higher.addEventListener('click', () => onChange(Math.min(maximum, value + 1)));
        stepper.append(lower, display, higher);
        return stepper;
      }

      _newInventoryEntityId(kind) {
        const token = typeof globalThis.crypto?.randomUUID === 'function'
          ? globalThis.crypto.randomUUID().toLowerCase().replaceAll('-', '.')
          : Array.from(globalThis.crypto.getRandomValues(new Uint32Array(4)), value => value.toString(16)).join('.');
        return `item.web.${kind}.${token}`;
      }

      _fraction(value) {
        if (!value || typeof value !== 'object' || Array.isArray(value) ||
          !Number.isSafeInteger(value.numerator) || !Number.isSafeInteger(value.denominator) ||
          value.denominator <= 0) return null;
        return value.denominator === 1 ? String(value.numerator) : `${value.numerator}/${value.denominator}`;
      }

      _inventoryComponent(entry, id) {
        const detail = entry?.components instanceof Map ? entry.components.get(id) : null;
        if (!detail) return {status: 'missing'};
        if (typeof detail.valueJson !== 'string') return {status: 'unavailable'};
        try {
          const value = JSON.parse(detail.valueJson);
          return value && typeof value === 'object' && !Array.isArray(value)
            ? {status: 'ok', value, revision: detail.revision}
            : {status: 'unavailable'};
        } catch { return {status: 'unavailable'}; }
      }

      _renderVitals() {
        const body = this._vitals.body;
        body.replaceChildren();
        body.className = 'panel-body vitals';
        const hp = this._component(DND2024_COMPONENTS.hitPoints);
        const temp = this._component(DND2024_COMPONENTS.temporaryHitPoints);
        const ac = this._component(DND2024_COMPONENTS.armorClass);
        const speed = this._component(DND2024_COMPONENTS.speed);

        const hpCard = this._element('div', 'vital');
        const heart = this._element('div', 'heart');
        const hpText = hp.status === 'ok' && Number.isInteger(hp.value.current) && Number.isInteger(hp.value.maximum)
          ? `${hp.value.current}/${hp.value.maximum}` : '—';
        heart.append(this._element('span', '', hp.status === 'ok' ? String(hp.value.current) : '—'));
        const hpCopy = this._element('div');
        hpCopy.append(this._element('strong', '', hpText), this._element('small', '', 'Hit points'));
        if (hp.status === 'ok' && hp.value.maximum > 0) {
          const meter = document.createElement('meter');
          meter.min = 0;
          meter.max = hp.value.maximum;
          meter.value = hp.value.current;
          meter.setAttribute('aria-label', `${hp.value.current} of ${hp.value.maximum} hit points`);
          hpCopy.append(meter);
        }
        hpCard.append(heart, hpCopy);

        const tempCard = this._vitalText(temp.status === 'ok' ? temp.value.amount : '—', 'Temporary HP', '✦');
        const acCard = this._element('div', 'vital');
        acCard.append(this._element('div', 'shield', ac.status === 'ok' ? String(ac.value.value) : '—'),
          this._smallCopy('Armor class'));
        const speedCard = this._vitalText(speed.status === 'ok' ? `${speed.value.walkFeet} ft` : '—', 'Walking speed', '➜');
        body.append(hpCard, tempCard, acCard, speedCard);
        this._renderVitalActions(body, hp, temp);
      }

      _renderVitalActions(body, hp, temp) {
        const hpValid = hp.status === 'ok' && Number.isSafeInteger(hp.value.current) &&
          Number.isSafeInteger(hp.value.maximum) && hp.value.current >= 0 &&
          hp.value.maximum >= 1 && hp.value.current <= hp.value.maximum;
        const tempValid = temp.status === 'missing' || (temp.status === 'ok' &&
          Number.isSafeInteger(temp.value.amount) && temp.value.amount >= 1);
        if (!this._actionsAvailable || !this.selectedStateSpaceId || !this.selectedEntityId ||
          !hpValid || !tempValid) return;

        const generation = ++this._vitalActionGeneration;
        const deck = this._element('section', 'vital-actions');
        deck.setAttribute('aria-label', 'Hit point actions');
        deck.addEventListener('application-receipt', event => {
          if (event.detail?.phase === 'complete') this._loadEntity();
        });

        const healing = this._element('section', 'vital-action');
        const healingHead = this._element('div', 'vital-action-head');
        healingHead.append(this._element('h4', '', 'Healing'),
          this._element('span', 'vital-action-note', `${hp.value.current}/${hp.value.maximum} HP now`));
        const healingStepper = this._vitalAmountStepper('Healing amount', this._healingAmount,
          () => { this._healingAmount = Math.max(1, this._healingAmount - 1); this._renderVitals(); },
          () => { this._healingAmount = Math.min(999, this._healingAmount + 1); this._renderVitals(); });
        const healingSlot = this._element('div', 'vital-action-slot');
        healing.append(healingHead, healingStepper, healingSlot);
        this._mountVitalAction(healingSlot, generation, {
          mechanicId: 'mechanic.dnd2024.healing.apply',
          label: `Prepare healing ${this._healingAmount}`,
          input: {amount: this._healingAmount}
        });

        const temporary = this._element('section', 'vital-action');
        const temporaryHead = this._element('div', 'vital-action-head');
        temporaryHead.append(this._element('h4', '', 'Temporary HP'),
          this._element('span', 'vital-action-note', temp.status === 'ok'
            ? `${temp.value.amount} Temporary HP now` : 'No current buffer'));
        const temporaryStepper = this._vitalAmountStepper('Temporary HP amount',
          this._temporaryHitPointsAmount,
          () => { this._temporaryHitPointsAmount = Math.max(1, this._temporaryHitPointsAmount - 1); this._renderVitals(); },
          () => { this._temporaryHitPointsAmount = Math.min(999, this._temporaryHitPointsAmount + 1); this._renderVitals(); });
        temporary.append(temporaryHead, temporaryStepper);
        if (temp.status === 'ok') {
          const choices = this._element('div', 'vital-choice-grid');
          choices.append(this._vitalChoice('Keep current', 'keep'),
            this._vitalChoice('Replace with incoming', 'replace'));
          temporary.append(choices);
        }
        const temporarySlot = this._element('div', 'vital-action-slot');
        temporary.append(temporarySlot);
        const temporaryInput = {mode: 'grant', amount: this._temporaryHitPointsAmount};
        if (temp.status === 'ok') temporaryInput.onExisting = this._temporaryHitPointsChoice;
        this._mountVitalAction(temporarySlot, generation, {
          mechanicId: 'mechanic.dnd2024.temporary-hit-points.write',
          label: temp.status === 'ok' ? `Prepare ${this._temporaryHitPointsChoice} choice` : 'Prepare Temporary HP grant',
          input: temporaryInput
        });
        if (temp.status === 'ok') {
          const expire = this._element('div', 'vital-expire');
          expire.append(this._element('p', 'vital-action-note', 'End the current buffer without changing Hit Points.'));
          const expireSlot = this._element('div', 'vital-action-slot');
          expire.append(expireSlot);
          temporary.append(expire);
          this._mountVitalAction(expireSlot, generation, {
            mechanicId: 'mechanic.dnd2024.temporary-hit-points.write',
            label: 'Prepare Temporary HP expiry',
            input: {mode: 'expire'}
          });
        }

        deck.append(healing, temporary);
        body.append(deck);
      }

      _vitalAmountStepper(label, value, lowerAction, higherAction) {
        const stepper = this._element('div', 'vital-stepper');
        const lower = this._element('button', '', '−');
        lower.type = 'button'; lower.disabled = value <= 1;
        lower.setAttribute('aria-label', `Decrease ${label.toLowerCase()}`);
        lower.addEventListener('click', lowerAction);
        const amount = this._element('strong', '', String(value));
        amount.setAttribute('aria-label', `${label}: ${value}`);
        const higher = this._element('button', '', '+');
        higher.type = 'button'; higher.disabled = value >= 999;
        higher.setAttribute('aria-label', `Increase ${label.toLowerCase()}`);
        higher.addEventListener('click', higherAction);
        stepper.append(lower, amount, higher);
        return stepper;
      }

      _vitalChoice(label, value) {
        const button = this._element('button', 'vital-choice', label);
        button.type = 'button';
        button.setAttribute('aria-pressed', String(this._temporaryHitPointsChoice === value));
        button.addEventListener('click', () => {
          this._temporaryHitPointsChoice = value;
          this._renderVitals();
        });
        return button;
      }

      async _mountVitalAction(slot, generation, configuration) {
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._vitalActionGeneration || !slot.isConnected) return;
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.selectedStateSpaceId);
        control.setAttribute('mechanic-id', configuration.mechanicId);
        control.textContent = configuration.label;
        control.roleEntityIds = {subject: this.selectedEntityId};
        control.input = configuration.input;
        slot.replaceChildren(control);
      }

      _vitalText(value, label, icon) {
        const card = this._element('div', 'vital');
        const symbol = this._element('div', 'token-icon', icon);
        symbol.setAttribute('aria-hidden', 'true');
        const copy = this._element('div');
        copy.append(this._element('strong', '', String(value)), this._element('small', '', label));
        card.append(symbol, copy);
        return card;
      }

      _smallCopy(label) {
        const copy = this._element('div');
        copy.append(this._element('small', '', label));
        return copy;
      }

      _renderAbilities() {
        const body = this._abilities.body;
        body.replaceChildren();
        body.className = 'panel-body abilities';
        const abilities = this._component(DND2024_COMPONENTS.abilities);
        const labels = [['str','Strength'], ['dex','Dexterity'], ['con','Constitution'], ['int','Intelligence'], ['wis','Wisdom'], ['cha','Charisma']];
        for (const [key, label] of labels) {
          const tile = this._element('div', 'ability');
          const score = abilities.status === 'ok' && Number.isInteger(abilities.value[key]) ? abilities.value[key] : '—';
          tile.setAttribute('aria-label', `${label} ${score}`);
          tile.append(this._element('span', 'ability-name', key),
            this._element('span', 'ability-score', String(score)),
            this._element('span', 'ability-caption', label));
          body.append(tile);
        }
      }

      _renderTurnBudget() {
        const body = this._turn.body;
        body.replaceChildren();
        body.className = 'panel-body';
        if (!this.selectedEntityId) {
          body.append(this._empty('Choose an adventurer to inspect turn resources.'));
          return;
        }
        const budget = this._component(DND2024_COMPONENTS.turnBudget);
        const order = this._encounterComponent(DND2024_COMPONENTS.encounterOrder);
        const turn = this._encounterComponent(DND2024_COMPONENTS.encounterTurn);
        let activeParticipantId = '';
        let participant = false;
        if (order.status === 'ok' && this._validEncounterOrder(order.value)) {
          participant = order.value.order.some(item => item.participantId === this.selectedEntityId);
          if (turn.status === 'ok' && this._validEncounterTurn(turn.value, order.value.order.length) &&
            turn.value.status === 'active') {
            activeParticipantId = order.value.order[turn.value.turnIndex].participantId;
          }
        }
        const control = document.createElement('dnd2024-turn-budget');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.selectedStateSpaceId);
        control.setAttribute('subject-entity-id', this.selectedEntityId);
        if (this.selectedEncounterId) control.setAttribute('encounter-entity-id', this.selectedEncounterId);
        control.context = {budgetStatus: budget.status, budget: budget.status === 'ok' ? budget.value : null,
          activeParticipantId, participant, turnActive: Boolean(activeParticipantId)};
        control.actionsAvailable = this._actionsAvailable;
        control.addEventListener('dnd2024-encounter-refresh', () => this._loadEntity());
        body.append(control);
      }

      _renderActions() {
        const body = this._actions.body;
        body.replaceChildren();
        body.className = 'panel-body action-table';
        if (!this._actionsAvailable) {
          body.append(this._empty('This campaign is readable, but its rules binding is older than the active D&D action set. Actions will unlock after an explicit migration.'));
          return;
        }
        if (!this.selectedStateSpaceId || !this.selectedEntityId) {
          body.append(this._empty('Choose an adventurer before preparing a table action.'));
          return;
        }
        const dice = document.createElement('dnd2024-dice-tray');
        dice.setAttribute('application-id', this.applicationId);
        dice.setAttribute('state-space-id', this.selectedStateSpaceId);
        const checks = document.createElement('dnd2024-action-panel');
        checks.setAttribute('application-id', this.applicationId);
        checks.setAttribute('state-space-id', this.selectedStateSpaceId);
        checks.setAttribute('subject-entity-id', this.selectedEntityId);
        body.append(dice, checks);
      }

      _renderConditions() {
        const body = this._conditions.body;
        body.replaceChildren();
        body.className = 'panel-body chips';
        const conditions = this._component(DND2024_COMPONENTS.conditions);
        if (conditions.status !== 'ok' || !Array.isArray(conditions.value.entries)) {
          body.append(this._empty(conditions.status === 'missing' ? 'Conditions are unknown.' : 'Conditions are unavailable.'));
          return;
        }
        if (!conditions.value.entries.length) {
          body.append(this._element('span', 'chip good', 'No active conditions'));
          return;
        }
        for (const item of conditions.value.entries) {
          if (!item || typeof item.condition !== 'string') continue;
          const label = item.condition === 'exhaustion' && Number.isInteger(item.level)
            ? `Exhaustion ${item.level}` : this._label(item.condition);
          body.append(this._element('span', 'chip condition', label));
        }
        if (!body.childElementCount) body.append(this._empty('Condition data is unavailable.'));
      }

      _renderSpeed() {
        const body = this._speed.body;
        body.replaceChildren();
        body.className = 'panel-body speed-list';
        const speed = this._component(DND2024_COMPONENTS.speed);
        if (speed.status !== 'ok') {
          body.append(this._empty(speed.status === 'missing' ? 'Movement speeds are unknown.' : 'Movement speeds are unavailable.'));
          return;
        }
        for (const [key, label] of [['walkFeet','walk'], ['climbFeet','climb'], ['swimFeet','swim'], ['flyFeet','fly'], ['burrowFeet','burrow']]) {
          const value = speed.value[key];
          if (!Number.isInteger(value) || (key !== 'walkFeet' && value === 0)) continue;
          const row = this._element('div', 'speed-item');
          row.append(this._element('span', '', label), this._element('strong', '', `${value} ft`));
          body.append(row);
        }
      }

      _renderProficiencies() {
        const body = this._proficiencies.body;
        body.replaceChildren();
        body.className = 'panel-body stack';
        const groups = [
          ['Skills', DND2024_COMPONENTS.skills, 'skills'],
          ['Saving throws', DND2024_COMPONENTS.savingThrows, 'abilities'],
          ['Weapons', DND2024_COMPONENTS.weapons, 'categories'],
          ['Tools', DND2024_COMPONENTS.tools, 'tools'],
          ['Languages', DND2024_COMPONENTS.languages, 'languages']
        ];
        for (const [title, id, key] of groups) {
          const group = this._element('div');
          group.append(this._element('p', 'group-title', title));
          const chips = this._element('div', 'chips');
          const component = this._component(id);
          const values = component.status === 'ok' && Array.isArray(component.value[key]) ? component.value[key] : null;
          if (!values) chips.append(this._element('span', 'unknown', 'Unknown'));
          else if (!values.length) chips.append(this._element('span', 'chip', 'None'));
          else for (const value of values) chips.append(this._element('span', 'chip', this._label(value)));
          group.append(chips);
          body.append(group);
        }
      }

      _renderMitigation() {
        const body = this._mitigation.body;
        body.replaceChildren();
        body.className = 'panel-body stack';
        const mitigation = this._component(DND2024_COMPONENTS.damageMitigation);
        const groups = [['Resistant', 'resistances', 'good'], ['Immune', 'immunities', 'good'], ['Vulnerable', 'vulnerabilities', 'warn']];
        for (const [title, key, tone] of groups) {
          const group = this._element('div');
          group.append(this._element('p', 'group-title', title));
          const chips = this._element('div', 'chips');
          const values = mitigation.status === 'ok' && Array.isArray(mitigation.value[key]) ? mitigation.value[key] : null;
          if (!values) chips.append(this._element('span', 'unknown', 'Unknown'));
          else if (!values.length) chips.append(this._element('span', 'chip', 'None'));
          else for (const value of values) chips.append(this._element('span', `chip ${tone}`, this._label(value)));
          group.append(chips);
          body.append(group);
        }
      }

      _empty(message) { return this._element('div', 'empty', message); }

      _renderCampaign() {
        const body = this._campaignPanel.body;
        body.replaceChildren();
        body.className = 'panel-body dossier';
        const campaign = this._campaign;
        if (!campaign || typeof campaign !== 'object') {
          body.append(this._empty('Choose a registered campaign to load its state.'));
          return;
        }
        const title = typeof campaign.title === 'string' && campaign.title.trim()
          ? campaign.title : this.selectedCampaignId || 'Campaign';
        const summary = this._element('div', 'dossier-copy');
        if (typeof campaign.premise === 'string' && campaign.premise.trim()) {
          const premise = this._element('article', 'dossier-entry');
          premise.append(this._element('strong', '', title), this._element('p', '', campaign.premise));
          summary.append(premise);
        }
        for (const [label, values] of [['Party goals', campaign.partyGoals], ['Tone and boundaries', campaign.toneAndBoundaries]]) {
          if (!Array.isArray(values)) continue;
          const text = values.filter(value => typeof value === 'string' && value.trim()).join('\n');
          if (!text) continue;
          const entry = this._element('article', 'dossier-entry');
          entry.append(this._element('strong', '', label), this._element('p', '', text));
          summary.append(entry);
        }
        body.append(summary.childElementCount ? summary : this._empty('Campaign details are recorded but unavailable to display.'));
      }

      _renderKnowledge() {
        const body = this._knowledgePanel.body;
        body.replaceChildren();
        body.className = 'panel-body knowledge-view';
        const knowledge = this._knowledge;
        if (knowledge.status === 'loading') {
          body.append(this._empty('Opening the remembered lore ledger…'));
          return;
        }
        if (knowledge.status === 'missing') {
          body.append(this._empty('Choose a registered campaign to open remembered lore.'));
          return;
        }
        if (knowledge.status === 'unavailable') {
          body.append(this._empty('Remembered lore is unavailable for this player and campaign.'));
          return;
        }
        if (knowledge.status === 'empty' || !knowledge.entries.length) {
          body.append(this._empty('No remembered lore has been recorded for this player yet.'));
          return;
        }

        const tools = this._element('div', 'knowledge-tools');
        const searchLabel = this._element('label', 'knowledge-search', 'Search remembered lore');
        const search = document.createElement('input');
        search.type = 'search';
        search.placeholder = 'Place, person, danger, custom…';
        search.value = this._knowledgeQuery;
        search.addEventListener('input', () => {
          this._knowledgeQuery = search.value;
          this._renderKnowledge();
          const next = this._knowledgePanel.body.querySelector('.knowledge-search input');
          if (next) { next.focus(); next.setSelectionRange(this._knowledgeQuery.length, this._knowledgeQuery.length); }
        });
        searchLabel.append(search);
        const kinds = this._element('div', 'knowledge-kinds');
        const availableKinds = ['all', ...new Set(knowledge.entries.map(value => value.presentationKind))];
        if (!availableKinds.includes(this._knowledgeKind)) this._knowledgeKind = 'all';
        for (const kind of availableKinds) {
          const button = this._element('button', 'knowledge-kind', this._knowledgeKindLabel(kind));
          button.type = 'button';
          button.setAttribute('aria-pressed', String(this._knowledgeKind === kind));
          button.addEventListener('click', () => { this._knowledgeKind = kind; this._renderKnowledge(); });
          kinds.append(button);
        }
        tools.append(searchLabel, kinds);
        body.append(tools);

        const query = this._knowledgeQuery.trim().toLocaleLowerCase();
        const shown = knowledge.entries.filter(value =>
          (this._knowledgeKind === 'all' || value.presentationKind === this._knowledgeKind) &&
          (!query || value.text.toLocaleLowerCase().includes(query)));
        const ledger = this._element('div', 'knowledge-ledger');
        for (const entry of shown) {
          const lines = entry.text.split('\n').map(value => value.trim()).filter(Boolean);
          const card = this._element('article', 'knowledge-card');
          const head = this._element('div', 'knowledge-card-head');
          const title = this._element('h4', 'knowledge-title', lines[0] || 'Remembered lore');
          const stance = this._element('span', 'knowledge-stance', this._label(entry.stance));
          stance.dataset.stance = entry.stance;
          head.append(title, stance);
          card.append(head);
          if (lines.length > 1) card.append(this._element('p', 'knowledge-copy', lines.slice(1).join('\n')));
          card.append(this._element('span', 'knowledge-mark', this._knowledgeKindLabel(entry.presentationKind)));
          ledger.append(card);
        }
        body.append(ledger.childElementCount ? ledger : this._empty('No remembered lore matches these controls.'));
      }

      _knowledgeKindLabel(kind) {
        if (kind === 'all') return 'All lore';
        if (kind === 'statement') return 'Lore';
        if (kind === 'rumour') return 'Rumours';
        if (kind === 'evidence') return 'Clues';
        if (kind === 'recognition') return 'Familiar';
        return this._label(kind);
      }

      _renderLocation() {
        const body = this._location.body;
        body.replaceChildren();
        body.className = 'panel-body';
        const context = this._currentContext;
        if (context.status === 'missing') {
          body.append(this._empty('Current location is not recorded for this character.'));
          return;
        }
        if (context.status !== 'ok' || !context.location?.entity || !context.location?.value) {
          body.append(this._empty('Current location context is unavailable.'));
          return;
        }
        const value = context.location.value;
        const card = this._element('article', 'location-card');
        card.append(
          this._element('span', 'location-kind', this._label(value.kind)),
          this._element('h4', 'location-name', context.location.entity.name ||
            context.location.entity.entityId || 'Unnamed location'),
          this._element('p', 'location-summary', value.summary),
          this._element('span', 'location-meta',
            `Active · ${this._label(context.containment.slot)} · containment r${context.containment.revision}`)
        );
        body.append(card);
      }

      _renderScenePeople() {
        const body = this._scenePeople.body;
        body.replaceChildren();
        body.className = 'panel-body people-view';
        const context = this._currentContext;
        if (context.status === 'missing') {
          body.append(this._empty('People here cannot be resolved until a current location is recorded.'));
          return;
        }
        if (context.status !== 'ok') {
          body.append(this._empty('People in the current scene are unavailable.'));
          return;
        }
        if (!Array.isArray(context.people) || !context.people.length) {
          body.append(this._empty('No present actors are recorded at this location.'));
          return;
        }
        const switcher = this._element('div', 'people-switcher');
        for (const person of context.people) {
          const entityId = person.entity?.entityId;
          const name = person.entity?.name || entityId || 'Unnamed person';
          if (typeof entityId !== 'string' || !entityId) continue;
          const selected = entityId === this._selectedScenePersonId;
          const button = this._element('button', 'person-card');
          button.type = 'button';
          button.setAttribute('aria-pressed', String(selected));
          button.setAttribute('aria-label', `Show ${name}`);
          const mark = this._element('span', 'person-mark', name.trim().slice(0, 1) || '?');
          mark.setAttribute('aria-hidden', 'true');
          const identity = this._element('span');
          identity.append(this._element('span', 'person-name', name),
            this._element('span', 'person-role', this._scenePersonRole(person.kind)));
          button.append(mark, identity);
          button.addEventListener('click', () => {
            this._selectedScenePersonId = entityId;
            this._renderScenePeople();
          });
          switcher.append(button);
        }
        body.append(switcher);

        const selected = context.people.find(person =>
          person.entity?.entityId === this._selectedScenePersonId) || context.people[0];
        const detail = this._element('article', 'person-detail');
        detail.append(this._element('h4', '', selected.entity?.name || selected.entity?.entityId || 'Unnamed person'),
          this._element('p', '', this._scenePersonDescription(selected)));
        body.append(detail);
        if (context.boundary === 'page-limit') {
          body.append(this._element('div', 'scene-boundary',
            'More entities are recorded at this location. This scene view shows the first 24 direct contents.'));
        } else if (context.boundary === 'partial') {
          body.append(this._element('div', 'scene-boundary',
            'One or more present entities could not be read. Available people remain shown.'));
        }
      }

      _scenePersonRole(kind) {
        if (kind === 'current-character') return 'Current character';
        if (kind === 'campaign-character') return 'Campaign character';
        return 'Recurring world actor';
      }

      _scenePersonDescription(person) {
        if (person.kind === 'current-character') {
          const profile = this._component(DND2024_COMPONENTS.characterProfile);
          const details = [];
          if (profile.status === 'ok' && typeof profile.value.pronouns === 'string') {
            details.push(`Pronouns: ${profile.value.pronouns}`);
          }
          if (profile.status === 'ok' && typeof profile.value.appearance === 'string') {
            details.push(profile.value.appearance);
          }
          return details.length ? details.join('\n') :
            'This is the selected character. Open Character for their full recorded sheet.';
        }
        if (person.kind === 'campaign-character') {
          return 'A campaign character recorded as present at this exact location.';
        }
        return 'A recurring world actor recorded as present here. Motive text is not exposed as player knowledge.';
      }

      _label(value) {
        return String(value).split('-').map(word => word ? word[0].toUpperCase() + word.slice(1) : '').join(' ');
      }

      _renderUnknownPanels(message) {
        this._name.textContent = 'Choose an adventurer';
        this._scope.textContent = `${this.applicationId} / no exact entity selected`;
        this._level.textContent = 'Level —';
        this._inventory = {status: 'missing', contents: [], boundary: null};
        this._clearCurrentContext();
        for (const target of [this._vitals, this._dossier, this._abilities, this._inventoryPanel,
          this._turn, this._actions, this._encounter,
          this._conditions, this._speed, this._proficiencies, this._mitigation,
          this._location, this._scenePeople]) {
          target.body.className = 'panel-body';
          target.body.replaceChildren(this._empty(message));
        }
        this._renderKnowledge();
      }

      _fail(message, error) {
        const code = error?.code || error?.message || 'DND2024_STATE_UNAVAILABLE';
        if (code === 'HTTP_403') message = 'Access to this private game state was denied.';
        if (code === 'CURSOR_STALE') message = 'The game state changed while loading. Refresh to read the latest state.';
        if (code === 'APPLICATION_UNKNOWN' || code === 'STATE_SPACE_UNKNOWN' || code === 'ENTITY_UNKNOWN')
          message = 'The selected game scope no longer exists.';
        if (code === 'STATE_SPACE_WRONG_APPLICATION')
          message = 'That campaign space is unavailable for this application.';
        this._entity = null;
        this._components = new Map();
        this._renderUnknownPanels(message);
        this._setStatus(message, true);
        this._status.dataset.errorCode = code;
        this._emit('dnd2024-error', {code});
      }
    }

    function dndActionElement(tag, className, text) {
      const node = document.createElement(tag);
      if (className) node.className = className;
      if (text !== undefined) node.textContent = text;
      return node;
    }

    function dndActionButton(text, active, onClick) {
      const button = dndActionElement('button', 'control-button', text);
      button.type = 'button';
      button.setAttribute('aria-pressed', String(active));
      button.addEventListener('click', onClick);
      return button;
    }

    function dndActionStyle() {
      const style = document.createElement('style');
      style.textContent = `:host{display:block;color:var(--dnd-ink,#f6eedb);font:inherit}.card{background:linear-gradient(145deg,rgba(53,42,37,.9),rgba(25,24,27,.96));border:1px solid var(--dnd-line,rgba(218,192,147,.24));border-radius:.8rem;display:grid;gap:.75rem;padding:.8rem}.heading{align-items:baseline;display:flex;gap:.6rem;justify-content:space-between}.heading h4{color:var(--dnd-gold,#e0b968);font-family:Georgia,serif;font-size:.95rem;letter-spacing:.06em;margin:0;text-transform:uppercase}.heading p,.note{color:var(--dnd-muted,#b9ad96);font-size:.72rem;line-height:1.4;margin:0}.control-grid{display:grid;gap:.45rem;grid-template-columns:repeat(6,minmax(2.6rem,1fr))}.control-button,.stepper button{background:#211e20;border:1px solid #755e40;border-radius:.55rem;color:inherit;cursor:pointer;font:inherit;font-size:.72rem;font-weight:800;min-height:2.45rem;padding:.35rem}.control-button[aria-pressed='true']{background:linear-gradient(145deg,#a94e42,#692d2a);border-color:#e0b968;color:#fff6dd}.control-button:hover,.stepper button:hover{filter:brightness(1.16)}.control-button:focus-visible,.stepper button:focus-visible,.source:focus-visible{outline:3px solid #f3d797;outline-offset:2px}.stepper{align-items:center;display:grid;gap:.45rem;grid-template-columns:2.45rem minmax(6rem,1fr) 2.45rem}.stepper strong{background:#171518;border:1px solid #655641;border-radius:.55rem;font-family:Georgia,serif;font-size:1.25rem;padding:.45rem;text-align:center}.notation{color:#f4dca3;font-family:Georgia,serif;font-size:1.45rem;margin:0;text-align:center}.source{background:#171518;border:1px solid #655641;border-radius:.55rem;color:inherit;font:inherit;padding:.58rem;width:100%}.action-slot{min-height:2.7rem}.warning{color:#ffd0b0;font-size:.74rem;line-height:1.4;margin:0}.mode-grid{display:grid;gap:.45rem;grid-template-columns:repeat(2,minmax(0,1fr))}.circumstances{display:grid;gap:.45rem;grid-template-columns:repeat(3,minmax(0,1fr))}.voluntary{align-items:center;display:flex;gap:.55rem}.voluntary .control-button{flex:1}@media(max-width:34rem){.control-grid{grid-template-columns:repeat(3,minmax(2.6rem,1fr))}.circumstances{grid-template-columns:1fr}.heading{align-items:flex-start;flex-direction:column}}`;
      return style;
    }

    class Dnd2024DiceTray extends HTMLElement {
      static get observedAttributes() { return ['application-id', 'state-space-id']; }

      constructor() {
        super();
        this._sides = 20;
        this._modifier = 0;
        this._actionGeneration = 0;
        this.attachShadow({mode: 'open'});
        this._render();
      }

      connectedCallback() { this._render(); }
      attributeChangedCallback(name, before, after) { if (before !== after && this.isConnected) this._render(); }
      get applicationId() { return this.getAttribute('application-id')?.trim() || 'dnd2024'; }
      get stateSpaceId() { return this.getAttribute('state-space-id')?.trim() || ''; }

      _render() {
        const card = dndActionElement('section', 'card');
        const heading = dndActionElement('div', 'heading');
        heading.append(dndActionElement('h4', '', 'Dice tray'), dndActionElement('p', '', 'One die · server rolled'));
        const dice = dndActionElement('div', 'control-grid');
        for (const sides of DND2024_DICE_SIDES) dice.append(dndActionButton(`d${sides}`, this._sides === sides, () => {
          this._sides = sides; this._render();
        }));
        const notation = dndActionElement('p', 'notation', `1d${this._sides}${this._modifier >= 0 ? '+' : ''}${this._modifier}`);
        const modifier = dndActionElement('div', 'stepper');
        const lower = dndActionButton('−', false, () => { this._modifier = Math.max(-99, this._modifier - 1); this._render(); });
        lower.setAttribute('aria-label', 'Decrease dice modifier'); lower.disabled = this._modifier <= -99;
        const value = dndActionElement('strong', '', `Modifier ${this._modifier >= 0 ? '+' : ''}${this._modifier}`);
        const higher = dndActionButton('+', false, () => { this._modifier = Math.min(99, this._modifier + 1); this._render(); });
        higher.setAttribute('aria-label', 'Increase dice modifier'); higher.disabled = this._modifier >= 99;
        modifier.append(lower, value, higher);
        const slot = dndActionElement('div', 'action-slot');
        card.append(heading, dice, notation, modifier, slot);
        this.shadowRoot.replaceChildren(dndActionStyle(), card);
        this._mountAction(slot, {
          mechanicId: 'mechanic.dnd2024.dice', label: `Prepare 1d${this._sides}${this._modifier >= 0 ? '+' : ''}${this._modifier}`,
          roles: {}, input: {count: 1, sides: this._sides, modifier: this._modifier}
        });
      }

      async _mountAction(slot, configuration) {
        const generation = ++this._actionGeneration;
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._actionGeneration || !slot.isConnected) return;
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.stateSpaceId);
        control.setAttribute('mechanic-id', configuration.mechanicId);
        control.textContent = configuration.label;
        control.roleEntityIds = configuration.roles;
        control.input = configuration.input;
        slot.replaceChildren(control);
      }
    }

    class Dnd2024ActionPanel extends HTMLElement {
      static get observedAttributes() { return ['application-id', 'state-space-id', 'subject-entity-id']; }

      constructor() {
        super();
        this._mode = 'check';
        this._ability = 'str';
        this._dc = 10;
        this._circumstance = '';
        this._source = '';
        this._voluntaryFailure = false;
        this._actionGeneration = 0;
        this.attachShadow({mode: 'open'});
        this._render();
      }

      connectedCallback() { this._render(); }
      attributeChangedCallback(name, before, after) { if (before !== after && this.isConnected) this._render(); }
      get applicationId() { return this.getAttribute('application-id')?.trim() || 'dnd2024'; }
      get stateSpaceId() { return this.getAttribute('state-space-id')?.trim() || ''; }
      get subjectEntityId() { return this.getAttribute('subject-entity-id')?.trim() || ''; }

      _render() {
        const card = dndActionElement('section', 'card');
        const heading = dndActionElement('div', 'heading');
        heading.append(dndActionElement('h4', '', 'Test table'), dndActionElement('p', '', 'Use stored character state'));
        const modes = dndActionElement('div', 'mode-grid');
        modes.append(dndActionButton('Raw ability check', this._mode === 'check', () => {
          this._mode = 'check'; this._voluntaryFailure = false; this._render();
        }), dndActionButton('Saving throw', this._mode === 'save', () => {
          this._mode = 'save'; this._render();
        }));
        const abilities = dndActionElement('div', 'control-grid');
        for (const [ability, label] of DND2024_ABILITIES) abilities.append(dndActionButton(ability.toUpperCase(), this._ability === ability, () => {
          this._ability = ability; this._render();
        }));
        const dc = dndActionElement('div', 'stepper');
        const lower = dndActionButton('−', false, () => { this._dc = Math.max(0, this._dc - 1); this._render(); });
        lower.setAttribute('aria-label', 'Lower difficulty class'); lower.disabled = this._dc <= 0;
        const value = dndActionElement('strong', '', `DC ${this._dc}`);
        const higher = dndActionButton('+', false, () => { this._dc = Math.min(30, this._dc + 1); this._render(); });
        higher.setAttribute('aria-label', 'Raise difficulty class'); higher.disabled = this._dc >= 30;
        dc.append(lower, value, higher);
        card.append(heading, modes, abilities, dc);
        if (this._mode === 'save') {
          const voluntary = dndActionElement('div', 'voluntary');
          voluntary.append(dndActionButton('Choose failure without rolling', this._voluntaryFailure, () => {
            this._voluntaryFailure = !this._voluntaryFailure;
            if (this._voluntaryFailure) { this._circumstance = ''; this._source = ''; }
            this._render();
          }));
          card.append(voluntary);
        }
        if (!this._voluntaryFailure) card.append(this._renderCircumstances());
        const slot = dndActionElement('div', 'action-slot');
        card.append(slot);
        this.shadowRoot.replaceChildren(dndActionStyle(), card);
        const input = this._input();
        if (input) this._mountAction(slot, input);
        else slot.append(dndActionElement('p', 'warning', 'Name the advantage or disadvantage source before preparing this action.'));
      }

      _renderCircumstances() {
        const section = dndActionElement('section');
        section.append(dndActionElement('p', 'note', 'Roll circumstance'));
        const choices = dndActionElement('div', 'circumstances');
        for (const [value, label] of [['', 'Normal'], ['advantage', 'Advantage'], ['disadvantage', 'Disadvantage']]) {
          choices.append(dndActionButton(label, this._circumstance === value, () => {
            this._circumstance = value; if (!value) this._source = ''; this._render();
          }));
        }
        section.append(choices);
        if (this._circumstance) {
          const source = document.createElement('input');
          source.className = 'source'; source.type = 'text'; source.maxLength = 240;
          source.placeholder = `${this._circumstance === 'advantage' ? 'Advantage' : 'Disadvantage'} source`;
          source.value = this._source;
          source.setAttribute('aria-label', source.placeholder);
          source.addEventListener('change', () => { this._source = source.value.trim(); this._render(); });
          section.append(source);
        }
        return section;
      }

      _input() {
        const input = {ability: this._ability, dc: this._dc};
        if (this._mode === 'save' && this._voluntaryFailure) return {...input, voluntaryFailure: true};
        if (this._circumstance) {
          if (!this._source) return null;
          input.rollCircumstances = [{kind: this._circumstance, source: this._source}];
        }
        return input;
      }

      async _mountAction(slot, input) {
        const generation = ++this._actionGeneration;
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._actionGeneration || !slot.isConnected) return;
        const isSavingThrow = this._mode === 'save';
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.stateSpaceId);
        control.setAttribute('mechanic-id', isSavingThrow ? 'mechanic.dnd2024.saving-throw' : 'mechanic.dnd2024.check.ability');
        control.textContent = isSavingThrow ? 'Prepare saving throw' : 'Prepare ability check';
        control.roleEntityIds = {subject: this.subjectEntityId};
        control.input = input;
        slot.replaceChildren(control);
      }
    }

    function dndEncounterStyle() {
      const style = document.createElement('style');
      style.textContent = `
        :host{display:block;color:var(--dnd-ink,#f6eedb);font:inherit}
        *{box-sizing:border-box}
        button:focus-visible{outline:3px solid #f3d797;outline-offset:2px}
        .tracker{display:grid;gap:.72rem}
        .tracker-head{align-items:center;display:flex;flex-wrap:wrap;gap:.45rem;justify-content:space-between}
        .badge{background:#322d2d;border:1px solid #665b50;border-radius:999px;color:#d8ccb6;font-size:.66rem;font-weight:850;letter-spacing:.06em;padding:.38rem .55rem;text-transform:uppercase}
        .badge[data-state='active']{background:#284432;border-color:#527c5d;color:#dff1df}
        .badge[data-state='ended']{background:#4a2b2b;border-color:#82504c;color:#f1cfca}
        .round{color:var(--dnd-muted,#b9ad96);font-size:.7rem}
        .initiative-list{display:grid;gap:.48rem;list-style:none;margin:0;padding:0}
        .initiative-card{align-items:center;background:#29272a;border:1px solid #5f5548;border-radius:.68rem;display:grid;gap:.55rem;grid-template-columns:auto minmax(0,1fr) auto;min-width:0;padding:.62rem;position:relative}
        .initiative-card[aria-current='step']{background:linear-gradient(100deg,#385443,#2a2c2b);border-color:#80ad86;box-shadow:inset .25rem 0 #9bc39e}
        .rank{align-items:center;border:1px solid #77664d;border-radius:50%;color:var(--dnd-gold,#e0b968);display:flex;font-family:Georgia,serif;font-weight:900;height:2rem;justify-content:center;width:2rem}
        .name{font-family:Georgia,serif;font-size:.9rem;font-weight:750;overflow-wrap:anywhere}
        .name small{color:var(--dnd-muted,#b9ad96);display:block;font-family:ui-monospace,monospace;font-size:.58rem;font-weight:400;margin-top:.12rem;overflow-wrap:anywhere}
        .score{color:#f4dca3;font-family:Georgia,serif;font-size:1.3rem;font-weight:900;text-align:right}
        .score small{color:var(--dnd-muted,#b9ad96);display:block;font-family:Inter,sans-serif;font-size:.55rem;letter-spacing:.06em;text-transform:uppercase}
        .current-flag{color:#dff1df;display:block;font-family:Inter,sans-serif;font-size:.56rem;letter-spacing:.06em;margin-top:.18rem;text-transform:uppercase}
        .controls{border-top:1px solid rgba(216,204,182,.14);display:grid;gap:.58rem;grid-template-columns:repeat(2,minmax(0,1fr));padding-top:.7rem}
        .controls.one{grid-template-columns:1fr}
        .action-slot{display:grid;gap:.45rem;min-height:2.7rem}
        .warning{color:#ffd0b0;font-size:.7rem;grid-column:1/-1;line-height:1.4;margin:0}
        .note{color:var(--dnd-muted,#b9ad96);font-size:.7rem;line-height:1.4;margin:0}
        @media(max-width:34rem){.controls{grid-template-columns:1fr}.initiative-card{grid-template-columns:auto minmax(0,1fr)}.score{grid-column:2;text-align:left}}
      `;
      return style;
    }

    class Dnd2024EncounterTracker extends HTMLElement {
      static get observedAttributes() {
        return ['application-id', 'state-space-id', 'encounter-entity-id'];
      }

      constructor() {
        super();
        this._context = null;
        this._actionsAvailable = false;
        this._actionGeneration = 0;
        this.attachShadow({mode: 'open'});
      }

      connectedCallback() { this._render(); }
      attributeChangedCallback(name, before, after) {
        if (before !== after && this.isConnected) this._render();
      }
      set context(value) { this._context = value; if (this.isConnected) this._render(); }
      set actionsAvailable(value) { this._actionsAvailable = value === true; if (this.isConnected) this._render(); }
      get applicationId() { return this.getAttribute('application-id')?.trim() || 'dnd2024'; }
      get stateSpaceId() { return this.getAttribute('state-space-id')?.trim() || ''; }
      get encounterEntityId() { return this.getAttribute('encounter-entity-id')?.trim() || ''; }

      _render() {
        const generation = ++this._actionGeneration;
        const tracker = dndActionElement('section', 'tracker');
        tracker.setAttribute('aria-label', 'Encounter turn tracker');
        tracker.addEventListener('application-receipt', event => {
          if (event.detail?.phase !== 'complete') return;
          this.dispatchEvent(new CustomEvent('dnd2024-encounter-refresh',
            {detail: {encounterEntityId: this.encounterEntityId}, bubbles: true, composed: true}));
        });
        const context = this._context;
        const entries = context?.order?.order;
        if (!Array.isArray(entries) || entries.length < 1 || entries.length > 100) {
          tracker.append(dndActionElement('p', 'note', 'Initiative order is unavailable.'));
          this.shadowRoot.replaceChildren(dndEncounterStyle(), tracker);
          return;
        }
        const names = context.participantNames instanceof Map ? context.participantNames : new Map();
        let status = 'not-started';
        let round = null;
        let turnIndex = null;
        if (context.turnStatus === 'unavailable') {
          tracker.append(dndActionElement('p', 'warning', 'Encounter turn state is unavailable.'));
        } else if (context.turnStatus === 'ok') {
          const turn = context.turn;
          const valid = turn && (turn.status === 'active' || turn.status === 'ended') &&
            Number.isSafeInteger(turn.round) && turn.round >= 1 && Number.isInteger(turn.turnIndex) &&
            turn.turnIndex >= 0 && turn.turnIndex < entries.length;
          if (!valid) tracker.append(dndActionElement('p', 'warning', 'Encounter turn state is unavailable.'));
          else { status = turn.status; round = turn.round; turnIndex = turn.turnIndex; }
        }
        const head = dndActionElement('div', 'tracker-head');
        const stateLabel = status === 'active' ? 'Encounter active' :
          status === 'ended' ? 'Encounter ended' : 'Turns not started';
        const badge = dndActionElement('span', 'badge', stateLabel); badge.dataset.state = status;
        head.append(badge, dndActionElement('span', 'round', round === null ? 'No round' : `Round ${round}`));
        tracker.append(head);
        const list = dndActionElement('ol', 'initiative-list');
        for (let index = 0; index < entries.length; index += 1) {
          const entry = entries[index];
          const card = dndActionElement('li', 'initiative-card');
          if (status === 'active' && index === turnIndex) card.setAttribute('aria-current', 'step');
          const identity = dndActionElement('span', 'name', names.get(entry.participantId) || entry.participantId);
          identity.append(dndActionElement('small', '', entry.participantId));
          if (status === 'active' && index === turnIndex) {
            identity.append(dndActionElement('span', 'current-flag', 'Current turn'));
          }
          const score = dndActionElement('span', 'score', String(entry.initiative));
          score.append(dndActionElement('small', '', 'Initiative'));
          card.append(dndActionElement('span', 'rank', String(index + 1)), identity, score);
          list.append(card);
        }
        tracker.append(list);
        if (!this._actionsAvailable) {
          tracker.append(dndActionElement('p', 'note', 'Turn controls are locked for this campaign binding.'));
        } else if (context.turnStatus === 'missing') {
          const controls = dndActionElement('div', 'controls one');
          const slot = dndActionElement('div', 'action-slot'); controls.append(slot); tracker.append(controls);
          this._mountAction(slot, generation, 'mechanic.dnd2024.encounter-turn.start', 'Prepare start turns');
        } else if (status === 'active') {
          const controls = dndActionElement('div', 'controls');
          controls.append(dndActionElement('p', 'warning',
            'Ending the encounter preserves its final round and current participant.'));
          const advance = dndActionElement('div', 'action-slot');
          const end = dndActionElement('div', 'action-slot');
          controls.append(advance, end); tracker.append(controls);
          this._mountAction(advance, generation, 'mechanic.dnd2024.encounter-turn.advance', 'Prepare next turn');
          this._mountAction(end, generation, 'mechanic.dnd2024.encounter-turn.end', 'Prepare end encounter');
        }
        this.shadowRoot.replaceChildren(dndEncounterStyle(), tracker);
      }

      async _mountAction(slot, generation, mechanicId, label) {
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._actionGeneration || !slot.isConnected) return;
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.stateSpaceId);
        control.setAttribute('mechanic-id', mechanicId);
        control.textContent = label;
        control.roleEntityIds = {encounter: this.encounterEntityId};
        control.input = {};
        slot.replaceChildren(control);
      }
    }

    function dndTurnBudgetStyle() {
      const style = document.createElement('style');
      style.textContent = `
        :host{display:block;color:var(--dnd-ink,#f6eedb);font:inherit}
        *{box-sizing:border-box}
        button:focus-visible{outline:3px solid #f3d797;outline-offset:2px}
        .budget{display:grid;gap:.72rem}
        .tokens{display:grid;gap:.55rem;grid-template-columns:repeat(5,minmax(5.2rem,1fr))}
        .token{align-items:center;background:#252326;border:1px solid #625849;border-radius:.72rem;color:inherit;display:flex;flex-direction:column;font:inherit;justify-content:center;min-height:5.2rem;padding:.55rem;text-align:center;width:100%}
        button.token{cursor:pointer}
        button.token:hover{filter:brightness(1.14)}
        .token[data-ready='true']{background:radial-gradient(circle at 50% 25%,#42694d,#23342a);border-color:#79aa82;box-shadow:inset 0 0 0 2px rgba(121,170,130,.08)}
        .token[aria-pressed='true']{border-color:#f3d797;box-shadow:0 0 0 2px rgba(243,215,151,.2),inset 0 0 0 2px rgba(243,215,151,.12)}
        .icon{align-items:center;border:2px solid currentColor;border-radius:50%;display:flex;font-family:Georgia,serif;font-size:1rem;font-weight:900;height:2rem;justify-content:center;margin-bottom:.3rem;width:2rem}
        .token strong{font-size:.66rem;letter-spacing:.04em;text-transform:uppercase}
        .token small{color:var(--dnd-muted,#b9ad96);font-size:.62rem;margin-top:.1rem}
        .spend{background:linear-gradient(145deg,rgba(53,42,37,.9),rgba(25,24,27,.96));border:1px solid rgba(218,192,147,.24);border-radius:.75rem;display:grid;gap:.6rem;padding:.7rem}
        .spend p{color:var(--dnd-muted,#b9ad96);font-size:.7rem;line-height:1.4;margin:0}
        .stepper{align-items:center;display:grid;gap:.45rem;grid-template-columns:2.45rem minmax(6rem,1fr) 2.45rem}
        .stepper button{background:#211e20;border:1px solid #755e40;border-radius:.55rem;color:inherit;cursor:pointer;font:inherit;font-weight:850;min-height:2.45rem}
        .stepper button:disabled{cursor:not-allowed;opacity:.45}
        .stepper strong{background:#171518;border:1px solid #655641;border-radius:.55rem;font-family:Georgia,serif;font-size:1.12rem;padding:.5rem;text-align:center}
        .action-slot{display:grid;gap:.45rem;min-height:2.7rem}
        .note{color:var(--dnd-muted,#b9ad96);font-size:.7rem;line-height:1.4;margin:0}
        @media(max-width:54rem){.tokens{grid-template-columns:repeat(3,minmax(0,1fr))}}
        @media(max-width:34rem){.tokens{grid-template-columns:repeat(2,minmax(0,1fr))}}
      `;
      return style;
    }

    class Dnd2024TurnBudget extends HTMLElement {
      static get observedAttributes() {
        return ['application-id', 'state-space-id', 'subject-entity-id', 'encounter-entity-id'];
      }

      constructor() {
        super();
        this._context = null;
        this._actionsAvailable = false;
        this._resource = '';
        this._movementFeet = 5;
        this._actionGeneration = 0;
        this.attachShadow({mode: 'open'});
      }

      connectedCallback() { this._render(); }
      attributeChangedCallback(name, before, after) {
        if (before !== after && this.isConnected) this._render();
      }
      set context(value) { this._context = value; if (this.isConnected) this._render(); }
      set actionsAvailable(value) { this._actionsAvailable = value === true; if (this.isConnected) this._render(); }
      get applicationId() { return this.getAttribute('application-id')?.trim() || 'dnd2024'; }
      get stateSpaceId() { return this.getAttribute('state-space-id')?.trim() || ''; }
      get subjectEntityId() { return this.getAttribute('subject-entity-id')?.trim() || ''; }
      get encounterEntityId() { return this.getAttribute('encounter-entity-id')?.trim() || ''; }

      _validBudget(value) {
        if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).length !== 6 ||
          !['action', 'bonusAction', 'reaction', 'freeInteraction'].every(key => typeof value[key] === 'boolean') ||
          !Number.isInteger(value.movementRemainingFeet) || value.movementRemainingFeet < 0 ||
          value.movementRemainingFeet > 1000 || !value.sourceRef ||
          Object.keys(value.sourceRef).length !== 2 ||
          value.sourceRef.sourceId !== 'source.dnd2024.srd-5.2.1' ||
          value.sourceRef.locator !== 'Playing the Game > Actions; Bonus Actions; Reactions; Interacting with Objects; Combat > Your Turn') {
          return false;
        }
        return true;
      }

      _render() {
        const generation = ++this._actionGeneration;
        const card = dndActionElement('section', 'budget');
        card.setAttribute('aria-label', 'Turn resource controls');
        card.addEventListener('application-receipt', event => {
          if (event.detail?.phase !== 'complete') return;
          this._resource = '';
          this._movementFeet = 5;
          this.dispatchEvent(new CustomEvent('dnd2024-encounter-refresh',
            {detail: {encounterEntityId: this.encounterEntityId, subjectEntityId: this.subjectEntityId},
              bubbles: true, composed: true}));
        });
        const context = this._context || {};
        const valid = context.budgetStatus === 'ok' && this._validBudget(context.budget);
        const budget = valid ? context.budget : null;
        const active = context.turnActive === true && context.participant === true &&
          context.activeParticipantId === this.subjectEntityId;
        const canUse = resource => this._actionsAvailable && Boolean(this.encounterEntityId) &&
          context.participant === true && context.turnActive === true &&
          (resource === 'reaction' || active);
        const definitions = [
          ['A', 'Action', 'action'], ['B', 'Bonus action', 'bonusAction'],
          ['R', 'Reaction', 'reaction'], ['I', 'Interaction', 'freeInteraction']
        ];
        const eligible = new Set();
        const tokens = dndActionElement('div', 'tokens');
        for (const [icon, label, resource] of definitions) {
          const ready = budget ? budget[resource] : null;
          const interactive = ready === true && canUse(resource);
          if (interactive) eligible.add(resource);
          const token = dndActionElement(interactive ? 'button' : 'div', 'token');
          if (interactive) {
            token.type = 'button';
            token.setAttribute('aria-pressed', String(this._resource === resource));
            token.addEventListener('click', () => { this._resource = resource; this._render(); });
          }
          token.dataset.ready = String(ready === true);
          token.append(dndActionElement('span', 'icon', icon), dndActionElement('strong', '', label),
            dndActionElement('small', '', ready === null ? 'Unknown' : ready ? interactive ? 'Ready · select' : 'Ready' : 'Spent'));
          tokens.append(token);
        }
        const movementRemaining = budget ? budget.movementRemainingFeet : null;
        const movementInteractive = Number.isInteger(movementRemaining) && movementRemaining >= 5 && canUse('movement');
        if (movementInteractive) eligible.add('movement');
        const movement = dndActionElement(movementInteractive ? 'button' : 'div', 'token');
        if (movementInteractive) {
          movement.type = 'button';
          movement.setAttribute('aria-pressed', String(this._resource === 'movement'));
          movement.addEventListener('click', () => { this._resource = 'movement'; this._render(); });
        }
        movement.dataset.ready = String(Number.isInteger(movementRemaining) && movementRemaining > 0);
        movement.append(dndActionElement('span', 'icon', '➜'), dndActionElement('strong', '', 'Movement'),
          dndActionElement('small', '', Number.isInteger(movementRemaining) ? `${movementRemaining} ft` : 'Unknown'));
        tokens.append(movement); card.append(tokens);
        if (!eligible.has(this._resource)) this._resource = '';
        if (!valid) {
          card.append(dndActionElement('p', 'note', context.budgetStatus === 'missing'
            ? 'Turn resources are not recorded for this participant.' : 'Turn resources are unavailable.'));
        } else if (!this._actionsAvailable) {
          card.append(dndActionElement('p', 'note', 'Resource controls are locked for this campaign binding.'));
        } else if (!this.encounterEntityId || context.turnActive !== true || context.participant !== true) {
          card.append(dndActionElement('p', 'note', 'Choose an active recorded encounter containing this participant.'));
        } else if (!eligible.size) {
          card.append(dndActionElement('p', 'note', active
            ? 'No displayed turn resource is currently available.' : 'Only a ready Reaction can be spent off turn.'));
        }
        if (this._resource) {
          const spend = dndActionElement('section', 'spend');
          let input;
          if (this._resource === 'movement') {
            const maximum = Math.floor(movementRemaining / 5) * 5;
            this._movementFeet = Math.min(maximum, Math.max(5, this._movementFeet));
            spend.append(dndActionElement('p', '', 'Spend 5-foot movement increments. Position and route remain separate.'));
            const stepper = dndActionElement('div', 'stepper');
            const lower = dndActionElement('button', '', '−'); lower.type = 'button';
            lower.disabled = this._movementFeet <= 5; lower.setAttribute('aria-label', 'Spend 5 fewer feet');
            lower.addEventListener('click', () => { this._movementFeet -= 5; this._render(); });
            const amount = dndActionElement('strong', '', `${this._movementFeet} ft`);
            const higher = dndActionElement('button', '', '+'); higher.type = 'button';
            higher.disabled = this._movementFeet >= maximum; higher.setAttribute('aria-label', 'Spend 5 more feet');
            higher.addEventListener('click', () => { this._movementFeet += 5; this._render(); });
            stepper.append(lower, amount, higher); spend.append(stepper);
            input = {resource: 'movement', feet: this._movementFeet};
          } else input = {resource: this._resource};
          const slot = dndActionElement('div', 'action-slot'); spend.append(slot); card.append(spend);
          this._mountAction(slot, generation, input);
        }
        this.shadowRoot.replaceChildren(dndTurnBudgetStyle(), card);
      }

      async _mountAction(slot, generation, input) {
        await customElements.whenDefined('application-action-button');
        if (!this.isConnected || generation !== this._actionGeneration || !slot.isConnected) return;
        const control = document.createElement('application-action-button');
        control.setAttribute('application-id', this.applicationId);
        control.setAttribute('state-space-id', this.stateSpaceId);
        control.setAttribute('mechanic-id', 'mechanic.dnd2024.turn-budget.spend');
        control.textContent = input.resource === 'movement'
          ? `Prepare spend ${input.feet} ft` : `Prepare spend ${String(input.resource).replace('freeInteraction', 'interaction')}`;
        control.roleEntityIds = {subject: this.subjectEntityId, encounter: this.encounterEntityId};
        control.input = input;
        slot.replaceChildren(control);
      }
    }

    if (!customElements.get('dnd2024-character-sheet')) {
      customElements.define('dnd2024-character-sheet', Dnd2024CharacterSheet);
    }
    if (!customElements.get('dnd2024-workspace')) {
      customElements.define('dnd2024-workspace', Dnd2024Workspace);
    }
    if (!customElements.get('dnd2024-dice-tray')) customElements.define('dnd2024-dice-tray', Dnd2024DiceTray);
    if (!customElements.get('dnd2024-action-panel')) customElements.define('dnd2024-action-panel', Dnd2024ActionPanel);
    if (!customElements.get('dnd2024-encounter-tracker')) {
      customElements.define('dnd2024-encounter-tracker', Dnd2024EncounterTracker);
    }
    if (!customElements.get('dnd2024-turn-budget')) {
      customElements.define('dnd2024-turn-budget', Dnd2024TurnBudget);
    }
