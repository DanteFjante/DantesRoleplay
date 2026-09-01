namespace DantesRoleplay.Web.Interactions;

public static class ApplicationConversationElement
{
    public const string Script = """
    const applicationConversationClient = import('/components/system-client.js')
      .then(module => module.systemWebClient);

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
        const emit = (name, detail) => this.dispatchEvent(new CustomEvent(name, {
          detail, bubbles: true, composed: true
        }));
        const requestJson = async (path, body, method = 'POST') => {
          const client = await applicationConversationClient;
          return await client.requestJson(path, {
            method, body, signal: this._request.signal
          });
        };
        let situationMediaRevision = 0;
        const renderSituationMedia = async (current, revision) => {
          const locationId = current && current.location && current.location.id;
          if (!locationId || !applicationId || !stateSpaceId) return;
          try {
            const encodedApplication = encodeURIComponent(applicationId);
            const encodedStateSpace = encodeURIComponent(stateSpaceId);
            const encodedLocation = encodeURIComponent(locationId);
            const ownerPath = `/api/applications/${encodedApplication}/state-spaces/${encodedStateSpace}` +
              `/entities/${encodedLocation}/media`;
            const value = await requestJson(ownerPath, undefined, 'GET');
            if (revision !== situationMediaRevision || !this.isConnected ||
                value.entityId !== locationId || !Array.isArray(value.attachments)) return;
            const allowedRoles = ['setting', 'scene', 'illustration', 'portrait', 'map', 'icon'];
            const allowedTypes = new Set(['image/png', 'image/jpeg', 'image/webp']);
            const contentPrefix = `${ownerPath}/`;
            const attachment = allowedRoles
              .map(role => value.attachments.find(item => item && item.role === role))
              .find(item => item && typeof item.mediaId === 'string' && item.mediaId.length > 0 &&
                allowedTypes.has(item.mediaType) && Number.isInteger(item.width) &&
                item.width > 0 && item.width <= 10000 && Number.isInteger(item.height) &&
                item.height > 0 && item.height <= 10000 && typeof item.alt === 'string' &&
                item.alt.length > 0 && item.alt.length <= 500 && typeof item.caption === 'string' &&
                item.caption.length <= 1000 && typeof item.contentUrl === 'string' &&
                item.contentUrl.startsWith(contentPrefix) && item.contentUrl.endsWith('/content'));
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
            caption.textContent = attachment.caption || attachment.alt;
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
          const mediaRevision = ++situationMediaRevision;
          shownMessages = value.messages || [];
          renderMessages();
          situation.replaceChildren();
          const current = value.currentSituation;
          const situationHeading = document.createElement('h3');
          situationHeading.textContent = 'Current situation';
          const situationText = document.createElement('p');
          if (!current) {
            situationText.textContent = 'No active situation.';
          } else {
            const people = (current.participants || []).map(person => person.name).join(', ');
            const location = current.location && current.location.name ? ` at ${current.location.name}` : '';
            situationText.textContent = `${current.kind}${location}: ${current.summary}${people ? ` — with ${people}` : ''}`;
          }
          situation.append(situationHeading, situationText);
          void renderSituationMedia(current, mediaRevision);
          earlier.hidden = !value.hasEarlierMessages;
          agenda.replaceChildren();
          if (value.activeAgenda) {
            const heading = document.createElement('p');
            heading.textContent = `Task plan: ${value.activeAgenda.status}`;
            agenda.append(heading);
            for (const task of value.activeAgenda.tasks || []) {
              const row = document.createElement('p');
              const completed = (task.batches || []).filter(batch => batch.status === 'completed').length;
              row.textContent = `Task ${task.ordinal}: ${task.status} (${completed}/${(task.batches || []).length} steps)`;
              agenda.append(row);
            }
          }
          status.textContent = value.status || '';
          confirm.hidden = value.status !== 'awaiting-confirmation';
          rememberLabel.hidden = value.status !== 'awaiting-confirmation';
          replaceLabel.hidden = !value.activeAgenda || ['completed', 'cancelled'].includes(value.activeAgenda.status);
          if (value.pendingPlan) emit('proposal', value.pendingPlan);
        };
        earlier.addEventListener('click', async () => {
          try {
            const first = shownMessages[0];
            if (!conversationId || !first) return;
            const page = await requestJson(
              `/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/history?beforeOrdinal=${encodeURIComponent(first.ordinal)}&limit=50`,
              undefined,
              'GET');
            const byOrdinal = new Map(shownMessages.map(message => [message.ordinal, message]));
            for (const message of page.messages || []) byOrdinal.set(message.ordinal, message);
            shownMessages = [...byOrdinal.values()].sort((left, right) => left.ordinal - right.ordinal);
            renderMessages();
            earlier.hidden = !page.nextBeforeOrdinal;
          } catch (error) { status.textContent = error.message; emit('error', {message:error.message}); }
        });
        const ensure = async () => {
          if (conversationId) return;
          if (!applicationId || !stateSpaceId || !sessionContextId) throw new Error('Application, state space, and session context are required.');
          emit('progress', {phase:'create'});
          const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations`,
            {stateSpaceId, sessionContextId});
          conversationId = value.id;
          show(value);
        };
        send.addEventListener('click', async () => {
          try {
            await ensure();
            emit('progress', {phase:'turn'});
            const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/turns`,
              {text: input.value, replaceActiveAgenda: replaceAgenda.checked});
            input.value = '';
            replaceAgenda.checked = false;
            show(value);
            emit('conversation-change', {conversationId, status:value.status,
              currentSituation:value.currentSituation, totalMessageCount:value.totalMessageCount});
          } catch (error) { status.textContent = error.message; emit('error', {message:error.message}); }
        });
        confirm.addEventListener('click', async () => {
          try {
            emit('progress', {phase:'execute'});
            const value = await requestJson(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/execute`,
              {learn: remember.checked});
            show(value);
            remember.checked = false;
            emit('receipt', value.lastExecution && value.lastExecution.receipt);
            emit('conversation-change', {conversationId, status:value.status,
              currentSituation:value.currentSituation, totalMessageCount:value.totalMessageCount});
          } catch (error) { status.textContent = error.message; emit('error', {message:error.message}); }
        });
        ensure().catch(error => { status.textContent = error.message; emit('error', {message:error.message}); });
      }
    }
    if (!customElements.get('application-conversation')) {
      customElements.define('application-conversation', ApplicationConversation);
    }
    """;
}
