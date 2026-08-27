    const DND2024_MAXIMUM_PAGES = 10;
    const DND2024_MAXIMUM_RECORDS = 1000;
    const DND2024_CHARACTER_FILTER_CONCURRENCY = 6;
    const DND2024_INVENTORY_MAXIMUM_DEPTH = 4;
    const DND2024_INVENTORY_MAXIMUM_ENTRIES = 96;
    const DND2024_INVENTORY_PAGE_SIZE = 24;
    const DND2024_DICE_SIDES = Object.freeze([4, 6, 8, 10, 12, 20]);
    const DND2024_ABILITIES = Object.freeze([
      ['str', 'Strength'], ['dex', 'Dexterity'], ['con', 'Constitution'],
      ['int', 'Intelligence'], ['wis', 'Wisdom'], ['cha', 'Charisma']
    ]);
    const DND2024_COMPONENTS = Object.freeze({
      abilities: 'dnd2024.abilities',
      armorClass: 'dnd2024.armor-class',
      characterExperience: 'dnd2024.character-experience',
      characterProfile: 'dnd2024.character.profile',
      conditions: 'dnd2024.conditions',
      creatureSize: 'dnd2024.creature-size',
      damageMitigation: 'dnd2024.damage-mitigation',
      encounterOrder: 'dnd2024.encounter-initiative-order',
      encounterTurn: 'dnd2024.encounter-turn-state',
      hitPoints: 'dnd2024.hit-points',
      itemDefinition: 'dnd2024.item-definition',
      itemInstance: 'dnd2024.item-instance',
      itemQuantity: 'dnd2024.item-quantity',
      level: 'dnd2024.character-level',
      languages: 'dnd2024.language-proficiencies',
      savingThrows: 'dnd2024.saving-throw-proficiencies',
      skills: 'dnd2024.skill-proficiencies',
      speed: 'dnd2024.speed',
      temporaryHitPoints: 'dnd2024.temporary-hit-points',
      tools: 'dnd2024.tool-proficiencies',
      turnBudget: 'dnd2024.turn-budget',
      weapons: 'dnd2024.weapon-proficiencies',
      equipmentState: 'dnd2024.equipment-state'
    });
    const DND2024_CHARACTER_COMPONENTS = Object.freeze([
      DND2024_COMPONENTS.abilities,
      DND2024_COMPONENTS.level
    ]);

    class Dnd2024Workspace extends HTMLElement {
      static get observedAttributes() { return ['application-id', 'state-space-id', 'entity-id']; }

      constructor() {
        super();
        this._connected = false;
        this._request = null;
        this._stateSpaces = [];
        this._entities = [];
        this._components = new Map();
        this._entity = null;
        this._inventory = {status: 'missing', contents: [], boundary: null};
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
        if (name === 'state-space-id') this._loadEntities();
        if (name === 'entity-id') this._loadEntity();
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
          .toolbar { align-items: end; background: rgba(12,12,14,.72); border: 1px solid var(--dnd-line); border-radius: .85rem; display: grid; gap: .65rem; grid-template-columns: minmax(9rem, 1fr) minmax(9rem, 1fr) auto; padding: .8rem; }
          label { color: var(--dnd-muted); display: grid; font-size: .68rem; font-weight: 800; gap: .3rem; letter-spacing: .08em; text-transform: uppercase; }
          select { background: #171719; border: 1px solid #635745; border-radius: .55rem; color: var(--dnd-ink); min-height: 2.65rem; min-width: 0; padding: .45rem .7rem; width: 100%; }
          .refresh { background: linear-gradient(#b44d43, #852d2a); border: 1px solid #d07162; border-radius: .55rem; color: white; cursor: pointer; font-weight: 800; min-height: 2.65rem; padding: .45rem 1rem; }
          .refresh:hover { filter: brightness(1.12); }
          .status { color: var(--dnd-muted); font-size: .78rem; margin: 0; min-height: 1.2em; }
          .status[data-error='true'] { color: #ffaaa2; }
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
          .inventory-boundary { background: rgba(94,74,42,.24); border-left: 3px solid #9d7f44; color: #d8ccb6; font-size: .68rem; line-height: 1.45; padding: .55rem .65rem; }
          .contained-card { align-items: center; background: #29272a; border: 1px dashed #655b50; border-radius: .62rem; display: flex; gap: .55rem; justify-content: space-between; min-width: 0; padding: .6rem; }
          .contained-card span { min-width: 0; overflow-wrap: anywhere; }
          .contained-card small { color: var(--dnd-muted); display: block; font-family: ui-monospace, monospace; font-size: .56rem; }
          .speed-list { display: grid; gap: .45rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
          .speed-item { align-items: center; background: #29272a; border-left: 3px solid var(--dnd-gold); border-radius: .35rem; display: flex; justify-content: space-between; padding: .55rem .65rem; }
          .speed-item span { color: var(--dnd-muted); font-size: .72rem; text-transform: capitalize; }
          .speed-item strong { font-family: Georgia, serif; }
          .empty { align-items: center; border: 1px dashed #655b50; border-radius: .6rem; color: var(--dnd-muted); display: flex; font-size: .76rem; min-height: 3.2rem; padding: .7rem; }
          .unknown { color: var(--dnd-muted); font-family: Georgia, serif; font-size: 1rem; font-style: italic; }
          [hidden] { display: none !important; }
          @media (max-width: 900px) {
            .dashboard { grid-template-columns: 1fr; }
            .vitals { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .abilities { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .tokens { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .dossier-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
            .inventory-grid { grid-template-columns: 1fr; }
          }
          @media (max-width: 560px) {
            .banner { grid-template-columns: auto minmax(0, 1fr); min-height: auto; }
            .crest { height: 3.7rem; width: 3.7rem; }
            .level { grid-column: 1 / -1; justify-self: start; }
            .toolbar { grid-template-columns: 1fr; }
            .vitals { grid-template-columns: 1fr 1fr; }
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
        const stateLabel = this._element('label', '', 'Campaign space');
        this._stateSpaceSelect = document.createElement('select');
        this._stateSpaceSelect.setAttribute('aria-label', 'Campaign state space');
        this._stateSpaceSelect.addEventListener('change', () => {
          this.removeAttribute('entity-id');
          this.setAttribute('state-space-id', this._stateSpaceSelect.value);
        });
        stateLabel.append(this._stateSpaceSelect);
        const entityLabel = this._element('label', '', 'Adventurer or creature');
        this._entitySelect = document.createElement('select');
        this._entitySelect.setAttribute('aria-label', 'Adventurer or creature');
        this._entitySelect.addEventListener('change', () => this.setAttribute('entity-id', this._entitySelect.value));
        entityLabel.append(this._entitySelect);
        this._refresh = this._element('button', 'refresh', 'Refresh state');
        this._refresh.type = 'button';
        this._refresh.addEventListener('click', () => this._load());
        toolbar.append(stateLabel, entityLabel, this._refresh);

        this._status = this._element('p', 'status', 'Loading current game state…');
        this._status.setAttribute('role', 'status');
        this._status.setAttribute('aria-live', 'polite');

        const dashboard = this._element('div', 'dashboard');
        const main = this._element('div', 'main');
        const side = this._element('aside', 'side');
        this._vitals = this._panel('Vitals', 'Current stored values');
        this._dossier = this._panel('Character dossier', 'Recorded identity · no inferred choices');
        this._abilities = this._panel('Ability scores', 'Server state · no local modifiers');
        this._inventoryPanel = this._panel('Inventory', 'Bounded nested contents · read-only');
        this._turn = this._panel('Turn resources', 'Read-only this slice');
        this._actions = this._panel('Action table', 'Server roll · review before execution');
        this._conditions = this._panel('Conditions', 'Active state');
        this._speed = this._panel('Movement', 'Base and remaining speed');
        this._proficiencies = this._panel('Proficiencies', 'Known training');
        this._mitigation = this._panel('Damage response', 'Resistance, immunity, vulnerability');
        this._encounter = this._panel('Encounter initiative', 'Stored order · selected encounter entity');
        main.append(this._vitals.panel, this._dossier.panel, this._abilities.panel,
          this._inventoryPanel.panel, this._turn.panel, this._actions.panel);
        side.append(this._encounter.panel, this._conditions.panel, this._speed.panel,
          this._proficiencies.panel, this._mitigation.panel);
        dashboard.append(main, side);
        this._shell.append(banner, toolbar, this._status, dashboard);
        this.shadowRoot.append(style, this._shell);
        this._renderUnknownPanels('Choose a current entity to reveal its game state.');
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
      }

      _emit(type, detail) {
        this.dispatchEvent(new CustomEvent(type, {detail, bubbles: true, composed: true}));
      }

      async _load() {
        if (this._request) this._request.abort();
        const request = new AbortController();
        this._request = request;
        this._refresh.disabled = true;
        this._setStatus('Loading playable campaign spaces…');
        this._emit('dnd2024-progress', {phase: 'state-spaces'});
        try {
          const root = `/api/applications/${encodeURIComponent(this.applicationId)}/state-spaces`;
          const candidates = await this._readAll(root, request.signal);
          this._stateSpaces = await this._findPlayableStateSpaces(candidates, request.signal);
          if (request !== this._request) return;
          this._fillSelect(this._stateSpaceSelect, this._stateSpaces, 'stateSpaceId', 'stateSpaceId',
            this.getAttribute('state-space-id'), 'No campaign spaces');
          if (!this._stateSpaceSelect.value) {
            this._entities = [];
            this._inventory = {status: 'missing', contents: [], boundary: null};
            this._fillSelect(this._entitySelect, [], 'entityId', 'name', null, 'No entities');
            this._renderUnknownPanels('No playable D&D campaign space is available.');
            this._setStatus('No playable D&D campaign space is available.');
            return;
          }
          await this._loadEntities(request);
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('Game state is unavailable. Check access and try again.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
      }

      async _loadEntities(existingRequest) {
        let request = existingRequest;
        if (!request) {
          if (this._request) this._request.abort();
          request = new AbortController();
          this._request = request;
          this._refresh.disabled = true;
        }
        const stateSpaceId = this.selectedStateSpaceId;
        if (!stateSpaceId) return;
        this._setStatus('Finding adventurers and creatures…');
        this._emit('dnd2024-progress', {phase: 'entities', stateSpaceId});
        try {
          const root = this._applicationRoot(stateSpaceId) + '/entities';
          const candidates = await this._readAll(root, request.signal);
          this._entities = await this._findDndCharacters(stateSpaceId, candidates, request.signal);
          if (request !== this._request) return;
          this._fillSelect(this._entitySelect, this._entities, 'entityId', 'name',
            this.getAttribute('entity-id'), 'No D&D characters');
          if (!this._entitySelect.value) {
            this._entity = null;
            this._components = new Map();
            this._inventory = {status: 'missing', contents: [], boundary: null};
            this._renderUnknownPanels('No D&D character record exists in this state space.');
            this._setStatus('No D&D character is available in this campaign space.');
            return;
          }
          await this._loadEntity(request);
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._fail('Entities are unavailable for this campaign space.', error);
        } finally {
          if (request === this._request) this._refresh.disabled = false;
        }
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
          const wanted = summaries.filter(item => Object.values(DND2024_COMPONENTS).includes(item.qualifiedTypeId));
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
          if (request !== this._request) return;
          this._renderState();
          const failed = settled.filter(result => result.status === 'rejected').length;
          const inventoryFailed = this._inventory.status === 'unavailable';
          this._setStatus(failed || inventoryFailed
            ? `Current state loaded; ${failed + (inventoryFailed ? 1 : 0)} panel value unavailable.`
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
        return {status: 'ok', name: content.name, value, summary: record.summary};
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

      _renderState() {
        this._name.textContent = this._entity?.name || this._entity?.entityId || 'Unnamed entity';
        this._scope.textContent = `${this.applicationId} / ${this.selectedStateSpaceId} / ${this.selectedEntityId} · entity r${this._entity?.revision ?? '—'}`;
        const level = this._component(DND2024_COMPONENTS.level);
        this._level.textContent = level.status === 'ok' && Number.isInteger(level.value.level)
          ? `Level ${level.value.level}` : 'Level —';
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
      }

      _renderDossier() {
        const body = this._dossier.body;
        body.replaceChildren();
        body.className = 'panel-body dossier';
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
        const order = this._component(DND2024_COMPONENTS.encounterOrder);
        const turn = this._component(DND2024_COMPONENTS.encounterTurn);
        if (order.status === 'missing') {
          body.append(this._empty('No Initiative snapshot is recorded on this entity.'));
          return;
        }
        const entries = order.status === 'ok' ? order.value.order : null;
        const orderValid = Array.isArray(entries) && entries.length >= 1 && entries.length <= 100 &&
          entries.every(item => item && typeof item.participantId === 'string' &&
            item.participantId.length > 0 && item.participantId.length <= 200 &&
            Number.isSafeInteger(item.initiative));
        if (!orderValid) {
          body.append(this._empty('Initiative order is unavailable.'));
          return;
        }

        let status = 'not-started';
        let round = null;
        let turnIndex = null;
        if (turn.status === 'unavailable') {
          body.append(this._empty('Encounter turn state is unavailable.'));
        } else if (turn.status === 'ok') {
          const value = turn.value;
          const valid = (value.status === 'active' || value.status === 'ended') &&
            Number.isInteger(value.round) && value.round >= 1 &&
            Number.isInteger(value.turnIndex) && value.turnIndex >= 0 && value.turnIndex < entries.length;
          if (!valid) {
            body.append(this._empty('Encounter turn state is unavailable.'));
          } else {
            status = value.status;
            round = value.round;
            turnIndex = value.turnIndex;
          }
        }

        const head = this._element('div', 'encounter-head');
        const stateLabel = status === 'active' ? 'Encounter active' :
          status === 'ended' ? 'Encounter ended' : 'Turns not started';
        const badge = this._element('span', 'encounter-badge', stateLabel);
        badge.dataset.state = status;
        head.append(badge, this._element('span', 'panel-note', round === null ? 'No round' : `Round ${round}`));
        body.append(head);

        const names = new Map(this._entities.map(entity => [entity.entityId, entity.name || entity.entityId]));
        const list = this._element('ol', 'initiative-list');
        for (let index = 0; index < entries.length; index += 1) {
          const item = entries[index];
          const card = this._element('li', 'initiative-card');
          if (status === 'active' && index === turnIndex) card.setAttribute('aria-current', 'step');
          const identity = this._element('div');
          identity.append(this._element('span', 'initiative-name', names.get(item.participantId) || item.participantId),
            this._element('span', 'initiative-id', item.participantId));
          const score = this._element('span', 'initiative-score', String(item.initiative));
          score.append(this._element('small', '', 'Initiative'));
          card.append(this._element('span', 'initiative-rank', String(index + 1)), identity, score);
          list.append(card);
        }
        body.append(list);
      }

      _renderInventory() {
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
          `Nested contents are read-only. This view shows at most ${DND2024_INVENTORY_MAXIMUM_ENTRIES} contents across ${DND2024_INVENTORY_MAXIMUM_DEPTH} levels.`));
      }

      _inventoryNode(entry, instance, depth) {
        const node = this._element('div', 'inventory-node');
        node.dataset.depth = String(depth);
        const containment = entry?.containment || {};
        if (instance) {
          node.append(this._inventoryCard(entry, instance));
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

      _inventoryCard(entry, instance) {
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
        return card;
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
        body.className = 'panel-body tokens';
        const budget = this._component(DND2024_COMPONENTS.turnBudget);
        const values = budget.status === 'ok' ? budget.value : null;
        const tokens = [
          ['A', 'Action', 'action'], ['B', 'Bonus action', 'bonusAction'],
          ['R', 'Reaction', 'reaction'], ['I', 'Interaction', 'freeInteraction']
        ];
        for (const [icon, label, key] of tokens) {
          const ready = values && typeof values[key] === 'boolean' ? values[key] : null;
          const token = this._element('div', 'token');
          token.dataset.ready = String(ready === true);
          token.append(this._element('span', 'token-icon', icon), this._element('strong', '', label),
            this._element('small', '', ready === null ? 'Unknown' : ready ? 'Ready' : 'Spent'));
          body.append(token);
        }
        const movement = this._element('div', 'token');
        const remaining = values && Number.isInteger(values.movementRemainingFeet) ? `${values.movementRemainingFeet} ft` : '—';
        movement.dataset.ready = String(values && values.movementRemainingFeet > 0);
        movement.append(this._element('span', 'token-icon', '➜'), this._element('strong', '', 'Movement'),
          this._element('small', '', remaining));
        body.append(movement);
      }

      _renderActions() {
        const body = this._actions.body;
        body.replaceChildren();
        body.className = 'panel-body action-table';
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
      _label(value) {
        return String(value).split('-').map(word => word ? word[0].toUpperCase() + word.slice(1) : '').join(' ');
      }

      _renderUnknownPanels(message) {
        this._name.textContent = 'Choose an adventurer';
        this._scope.textContent = `${this.applicationId} / no exact entity selected`;
        this._level.textContent = 'Level —';
        this._inventory = {status: 'missing', contents: [], boundary: null};
        for (const target of [this._vitals, this._dossier, this._abilities, this._inventoryPanel,
          this._turn, this._actions, this._encounter,
          this._conditions, this._speed, this._proficiencies, this._mitigation]) {
          target.body.className = 'panel-body';
          target.body.replaceChildren(this._empty(message));
        }
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

    if (!customElements.get('dnd2024-workspace')) {
      customElements.define('dnd2024-workspace', Dnd2024Workspace);
    }
    if (!customElements.get('dnd2024-dice-tray')) customElements.define('dnd2024-dice-tray', Dnd2024DiceTray);
    if (!customElements.get('dnd2024-action-panel')) customElements.define('dnd2024-action-panel', Dnd2024ActionPanel);
