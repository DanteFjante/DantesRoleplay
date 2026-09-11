const applicationConversationClient = import('/components/system-client.js')
  .then(module => module.systemWebClient);

// Display fields are independent. These helpers never supply command prerequisites.
const own = (value, key) => value && typeof value === 'object' && !Array.isArray(value) &&
  Object.hasOwn(value, key) ? value[key] : undefined;
const displayText = (value, fallback = '', maximum = 4000) =>
  typeof value === 'string' && value.length <= maximum ? value : fallback;
const positiveOrdinal = value => Number.isSafeInteger(value) && value > 0;
const recordId = value => typeof value === 'string' && value.length > 0 && value.length <= 200 &&
  value === value.trim() && !/[\u0000-\u001f\u007f/\\]/.test(value);
const displayRows = (value, maximum) => Array.isArray(value) && value.length <= maximum ? value : [];
const messageCharacters = rows => rows.reduce((total, row) => total + row.role.length + row.text.length + 64, 0);

function displayMessages(value, beforeOrdinal = Infinity, maximum = 64) {
  const candidates = displayRows(value, maximum);
  const counts = new Map();
  for (const row of candidates) {
    const ordinal = own(row, 'ordinal');
    if (positiveOrdinal(ordinal)) counts.set(ordinal, (counts.get(ordinal) ?? 0) + 1);
  }
  const rows = [];
  for (const row of candidates) {
    const ordinal = own(row, 'ordinal');
    if (!positiveOrdinal(ordinal) || ordinal >= beforeOrdinal || counts.get(ordinal) !== 1) continue;
    rows.push({ ordinal, role: displayText(own(row, 'role'), 'Speaker unavailable', 80),
      text: displayText(own(row, 'text'), 'Message text unavailable.', 32000) });
  }
  rows.sort((left, right) => left.ordinal - right.ordinal);
  if (messageCharacters(rows) > 300000) return { rows: [], partial: true };
  return { rows, partial: !Array.isArray(value) || candidates.length !== value.length || rows.length !== candidates.length };
}

class ApplicationConversation extends HTMLElement {
  constructor() {
    super();
    this._request = null;
  }

  disconnectedCallback() {
    this._request?.abort();
    this._request = null;
  }

  connectedCallback() {
    this._request?.abort();
    this._request = new AbortController();
    const controller = this._request;
    const isCurrent = () => this._request === controller && !controller.signal.aborted && this.isConnected;
    const assertCurrent = () => { if (!isCurrent()) throw new DOMException('Conversation view retired.', 'AbortError'); };
    this.replaceChildren();
    const situation = document.createElement('section');
    situation.setAttribute('aria-label', 'Current situation');
    const log = document.createElement('div');
    log.setAttribute('role', 'log');
    const earlier = document.createElement('button');
    earlier.textContent = 'Load earlier interactions';
    earlier.hidden = true;
    const input = document.createElement('textarea');
    input.placeholder = 'What do you want to do?';
    const agenda = document.createElement('div');
    agenda.setAttribute('aria-label', 'Task progress');
    const send = document.createElement('button');
    send.textContent = 'Send';
    const replaceLabel = document.createElement('label');
    const replaceAgenda = document.createElement('input');
    replaceAgenda.type = 'checkbox';
    replaceAgenda.checked = false;
    replaceLabel.append(replaceAgenda, document.createTextNode(' Replace unfinished task plan'));
    replaceLabel.hidden = true;
    const confirm = document.createElement('button');
    confirm.textContent = 'Confirm actions';
    confirm.hidden = true;
    const rememberLabel = document.createElement('label');
    const remember = document.createElement('input');
    remember.type = 'checkbox';
    remember.checked = false;
    rememberLabel.append(remember, document.createTextNode(' Remember this route'));
    rememberLabel.hidden = true;
    const status = document.createElement('p');
    this.append(situation, earlier, log, agenda, input, replaceLabel, send, rememberLabel, confirm, status);
    const applicationId = this.getAttribute('application-id');
    const stateSpaceId = this.getAttribute('state-space-id');
    const sessionContextId = this.getAttribute('session-context-id');
    let conversationId = null;
    let shownMessages = [];
    let viewRevision = 0;
    let historyCursor = null;
    let creating = null;
    let busy = false;
    let historyBusy = false;
    const emit = (name, detail) => isCurrent() && this.dispatchEvent(new CustomEvent(name, {
      detail, bubbles: true, composed: true
    }));
    const requestJson = async (path, body, method = 'POST') => {
      const client = await applicationConversationClient;
      assertCurrent();
      const result = await client.requestJson(path, {
        method, body, signal: controller.signal
      });
      assertCurrent();
      return result;
    };
    const showError = error => {
      if (!isCurrent() || error?.name === 'AbortError') return;
      const message = displayText(error?.message, 'The request could not be completed.');
      status.textContent = message;
      emit('error', { message });
    };
    const setBusy = value => {
      busy = value;
      send.disabled = value;
      confirm.disabled = value;
      earlier.disabled = value || historyBusy;
    };
    let situationMediaRevision = 0;
    const renderSituationMedia = async (current, revision) => {
      const locationId = own(own(current, 'location'), 'id');
      if (!recordId(locationId) || !applicationId || !stateSpaceId) return;
      try {
        const encodedApplication = encodeURIComponent(applicationId);
        const encodedStateSpace = encodeURIComponent(stateSpaceId);
        const encodedLocation = encodeURIComponent(locationId);
        const ownerPath = `/api/applications/${encodedApplication}/state-spaces/${encodedStateSpace}` +
          `/entities/${encodedLocation}/media`;
        const value = await requestJson(ownerPath, undefined, 'GET');
        if (revision !== situationMediaRevision || !isCurrent() ||
            own(value, 'entityId') !== locationId || !Array.isArray(own(value, 'attachments')) || value.attachments.length > 64) return;
        const allowedRoles = ['setting', 'scene', 'illustration', 'portrait', 'map', 'icon'];
        const allowedTypes = new Set(['image/png', 'image/jpeg', 'image/webp']);
        const contentPrefix = `${ownerPath}/`;
        const attachment = allowedRoles
          .flatMap(role => value.attachments.filter(item => own(item, 'role') === role))
          .find(item => {
            if (!['mediaId', 'mediaType', 'width', 'height', 'alt', 'contentUrl'].every(key => own(item, key) !== undefined)) return false;
            let content;
            try { content = new URL(item.contentUrl, window.location.origin); } catch { return false; }
            return recordId(item.mediaId) &&
            allowedTypes.has(item.mediaType) && Number.isInteger(item.width) &&
            item.width > 0 && item.width <= 10000 && Number.isInteger(item.height) &&
            item.height > 0 && item.height <= 10000 && typeof item.alt === 'string' &&
            item.alt.length > 0 && item.alt.length <= 500 && typeof item.contentUrl === 'string' &&
            item.contentUrl.startsWith(contentPrefix) && item.contentUrl.endsWith('/content') &&
            content.origin === window.location.origin && content.pathname.startsWith(contentPrefix) &&
            content.pathname.endsWith('/content') && !content.search && !content.hash;
          });
        if (!attachment) return;
        const card = document.createElement('figure');
        card.className = 'application-conversation__location-media';
        card.dataset.entityId = locationId;
        card.dataset.mediaRole = attachment.role;
        const image = document.createElement('img');
        image.src = attachment.contentUrl;
        image.alt = attachment.alt;
        image.width = attachment.width;
        image.height = attachment.height;
        image.loading = 'eager';
        image.decoding = 'async';
        const caption = document.createElement('figcaption');
        caption.textContent = displayText(own(attachment, 'caption'), '', 1000) || attachment.alt;
        card.append(image, caption);
        situation.append(card);
        emit('location-media', {
          entityId: locationId,
          mediaId: attachment.mediaId,
          role: attachment.role,
          contentUrl: attachment.contentUrl
        });
      } catch (error) {
        if (error && error.name === 'AbortError') return;
        // Media is optional and audience-filtered. Its absence must not block play or reveal why it was withheld.
      }
    };
    const renderMessages = () => {
      log.replaceChildren();
      for (const message of shownMessages) {
        const line = document.createElement('p');
        line.textContent = `${message.role}: ${message.text}`;
        log.append(line);
      }
    };
    const show = value => {
      assertCurrent();
      ++viewRevision;
      const mediaRevision = ++situationMediaRevision;
      const messages = displayMessages(own(value, 'messages'));
      shownMessages = messages.rows;
      renderMessages();
      if (messages.partial) {
        const warning = document.createElement('p');
        warning.textContent = 'Some interactions are unavailable for this read.';
        log.append(warning);
      }
      situation.replaceChildren();
      const current = own(value, 'currentSituation');
      const situationHeading = document.createElement('h3');
      situationHeading.textContent = 'Current situation';
      const situationText = document.createElement('p');
      if (current === null) {
        situationText.textContent = 'No active situation.';
      } else if (!current || typeof current !== 'object' || Array.isArray(current)) {
        situationText.textContent = 'Current situation unavailable.';
      } else {
        const rawPeople = own(current, 'participants');
        const peopleRows = displayRows(rawPeople, 64);
        const names = peopleRows
          .map(person => displayText(own(person, 'name'), '', 400)).filter(Boolean).join(', ');
        const participantsPartial = !Array.isArray(rawPeople) || rawPeople.length > 64 ||
          peopleRows.some(person => typeof own(person, 'name') !== 'string' || person.name.length > 400);
        const name = displayText(own(own(current, 'location'), 'name'), '', 400);
        const location = name ? ` at ${name}` : '';
        situationText.textContent = `${displayText(own(current, 'kind'), 'Situation', 80)}${location}: ${displayText(own(current, 'summary'), 'Summary unavailable.')}${names ? ` — with ${names}` : ''}${participantsPartial ? ' Some participant details are unavailable.' : ''}`;
      }
      situation.append(situationHeading, situationText);
      void renderSituationMedia(current, mediaRevision);
      historyCursor = own(value, 'hasEarlierMessages') === true && shownMessages.length ? shownMessages[0].ordinal : null;
      earlier.hidden = historyCursor === null;
      agenda.replaceChildren();
      const activeAgenda = own(value, 'activeAgenda');
      const agendaStatus = own(activeAgenda, 'status');
      if (activeAgenda && typeof activeAgenda === 'object' && !Array.isArray(activeAgenda)) {
        const heading = document.createElement('p');
        heading.textContent = `Task plan: ${displayText(agendaStatus, 'Status unavailable', 80)}`;
        agenda.append(heading);
        const tasks = displayRows(own(activeAgenda, 'tasks'), 64);
        for (const task of tasks) {
          if (!task || typeof task !== 'object' || Array.isArray(task)) continue;
          const row = document.createElement('p');
          const batches = displayRows(own(task, 'batches'), 128);
          const validBatches = Array.isArray(own(task, 'batches')) && task.batches.length <= 128 &&
            batches.every(batch => typeof own(batch, 'status') === 'string');
          const completed = batches.filter(batch => own(batch, 'status') === 'completed').length;
          row.textContent = `Task ${positiveOrdinal(own(task, 'ordinal')) ? task.ordinal : '?'}: ${displayText(own(task, 'status'), 'Status unavailable', 80)} (${validBatches ? `${completed}/${batches.length} steps` : 'Step progress unavailable'})`;
          agenda.append(row);
        }
        if (!Array.isArray(own(activeAgenda, 'tasks')) || activeAgenda.tasks.length > 64 ||
            tasks.some(task => !task || typeof task !== 'object' || Array.isArray(task))) {
          const warning = document.createElement('p');
          warning.textContent = 'Some task progress is unavailable for this read.';
          agenda.append(warning);
        }
      }
      const currentStatus = own(value, 'status');
      status.textContent = displayText(currentStatus, 'Conversation status unavailable.', 80);
      confirm.hidden = currentStatus !== 'awaiting-confirmation';
      rememberLabel.hidden = currentStatus !== 'awaiting-confirmation';
      replaceLabel.hidden = !['planning', 'awaiting-confirmation', 'needs-attention'].includes(agendaStatus);
      if (own(value, 'pendingPlan')) emit('proposal', value.pendingPlan);
    };
    earlier.addEventListener('click', async () => {
      if (busy || historyBusy || !isCurrent()) return;
      const revision = viewRevision;
      const beforeOrdinal = historyCursor;
      if (!conversationId || !positiveOrdinal(beforeOrdinal)) return;
      historyBusy = true;
      earlier.disabled = true;
      try {
        const page = await requestJson(
          `/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/history?beforeOrdinal=${beforeOrdinal}&limit=50`,
          undefined,
          'GET');
        if (revision !== viewRevision) return;
        const projected = displayMessages(own(page, 'messages'), beforeOrdinal, 50);
        const byOrdinal = new Map(shownMessages.map(message => [message.ordinal, message]));
        for (const message of projected.rows) if (!byOrdinal.has(message.ordinal)) byOrdinal.set(message.ordinal, message);
        const messages = [...byOrdinal.values()].sort((left, right) => left.ordinal - right.ordinal);
        // The widget is a bounded window, not a second unbounded history store.
        if (messages.length > 500 || messageCharacters(messages) > 300000) {
          historyCursor = null;
          earlier.hidden = true;
          status.textContent = 'The interaction history window is full.';
          return;
        }
        shownMessages = messages;
        renderMessages();
        const next = own(page, 'nextBeforeOrdinal');
        historyCursor = positiveOrdinal(next) && next < beforeOrdinal && projected.rows.length &&
          next <= projected.rows[0].ordinal ? next : null;
        earlier.hidden = historyCursor === null;
        if (projected.partial || (next !== null && historyCursor === null)) status.textContent = 'Some earlier interactions are unavailable for this read.';
      } catch (error) { showError(error); }
      finally { historyBusy = false; if (isCurrent()) earlier.disabled = busy; }
    });
    const ensure = async () => {
      if (conversationId) return;
      if (creating) return creating;
      if (![applicationId, stateSpaceId, sessionContextId].every(recordId)) throw new Error('Application, state space, and session context are required.');
      creating = (async () => {
        emit('progress', {phase:'create'});
        const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations`,
          {stateSpaceId, sessionContextId});
        if (!recordId(own(value, 'id'))) throw new Error('Conversation identity unavailable.');
        conversationId = value.id;
        show(value);
      })();
      try { await creating; } finally { creating = null; }
    };
    send.addEventListener('click', async () => {
      if (busy || !isCurrent()) return;
      const submittedText = input.value;
      const submittedReplace = replaceAgenda.checked;
      setBusy(true);
      try {
        await ensure();
        emit('progress', {phase:'turn'});
        const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/turns`,
          {text: submittedText, replaceActiveAgenda: submittedReplace});
        if (input.value === submittedText) input.value = '';
        if (replaceAgenda.checked === submittedReplace) replaceAgenda.checked = false;
        show(value);
        emit('conversation-change', {conversationId, status:own(value, 'status'),
          currentSituation:own(value, 'currentSituation'), totalMessageCount:own(value, 'totalMessageCount')});
      } catch (error) { showError(error); }
      finally { if (isCurrent()) setBusy(false); }
    });
    confirm.addEventListener('click', async () => {
      if (busy || !isCurrent() || confirm.hidden || !conversationId) return;
      setBusy(true);
      try {
        emit('progress', {phase:'execute'});
        const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/execute`,
          {learn: remember.checked});
        show(value);
        remember.checked = false;
        emit('receipt', own(own(value, 'lastExecution'), 'receipt'));
        emit('conversation-change', {conversationId, status:own(value, 'status'),
          currentSituation:own(value, 'currentSituation'), totalMessageCount:own(value, 'totalMessageCount')});
      } catch (error) { showError(error); }
      finally { if (isCurrent()) setBusy(false); }
    });
    ensure().catch(showError);
  }
}
if (!customElements.get('application-conversation')) {
  customElements.define('application-conversation', ApplicationConversation);
}
