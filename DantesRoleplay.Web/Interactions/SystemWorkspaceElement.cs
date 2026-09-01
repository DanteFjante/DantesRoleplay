namespace DantesRoleplay.Web.Interactions;

/// <summary>Host-owned browser components shared by trusted system and application pages.</summary>
public static class SystemWorkspaceElement
{
    public const string Script = """
    import {systemWebClient, validSystemIdentifier} from '/components/system-client.js';
    import '/components/system-publication.js';
    import '/components/ai-workspace.js';
    import '/components/page-administration.js';

    const CONTROL_CENTER_PATH = '/ui/control-center/index.html';

    class SystemNavigation extends HTMLElement {
      static get observedAttributes() { return ['application-id']; }

      constructor() {
        super();
        this._applications = [];
        this._connected = false;
        this._request = null;
        this._client = systemWebClient;
        this._routeChanged = () => this._updateCurrent();
        this.attachShadow({mode: 'open'});
        this._renderShell();
      }

      connectedCallback() {
        if (this._connected) return;
        this._connected = true;
        window.addEventListener('hashchange', this._routeChanged);
        window.addEventListener('popstate', this._routeChanged);
        this._updateCurrent();
        this._loadApplications();
      }

      disconnectedCallback() {
        this._connected = false;
        window.removeEventListener('hashchange', this._routeChanged);
        window.removeEventListener('popstate', this._routeChanged);
        if (this._request) this._request.abort();
        this._request = null;
      }

      attributeChangedCallback() {
        if (this.shadowRoot) this._updateCurrent();
      }

      set client(value) {
        if (!value || typeof value.discoverAllApplications !== 'function') throw new TypeError(
          'system-navigation requires an application discovery client.');
        this._client = value;
        if (this._connected) this._loadApplications();
      }

      get client() { return this._client; }

      _renderShell() {
        const style = document.createElement('style');
        style.textContent = `
          :host { display: block; color: var(--system-navigation-color, inherit); font: inherit; }
          nav, [part='system-pages'], [part='applications'] { display: flex; flex-direction: var(--system-navigation-direction, row); flex-wrap: wrap; gap: var(--system-navigation-gap, .45rem); align-items: var(--system-navigation-align, center); }
          a, button { box-sizing: border-box; border: 1px solid transparent; border-radius: var(--system-navigation-radius, 999px); color: var(--system-navigation-link-color, inherit); font: inherit; font-size: var(--system-navigation-font-size, .85rem); line-height: 1.25; padding: var(--system-navigation-padding, .5rem .75rem); text-decoration: none; }
          a:hover, a:focus-visible, button:hover:not(:disabled), button:focus-visible { border-color: var(--system-navigation-border-color, currentColor); outline: 2px solid var(--system-navigation-focus-color, currentColor); outline-offset: 2px; }
          a[aria-current='page'], button[data-current='true'] { background: var(--system-navigation-current-background, rgba(128, 170, 128, .16)); border-color: var(--system-navigation-current-border-color, currentColor); }
          [part='application'] { display: inline-flex; align-items: center; gap: .15rem; position: relative; }
          [part='application'][data-current='true'] { border-radius: var(--system-navigation-radius, 999px); box-shadow: 0 0 0 1px var(--system-navigation-current-border-color, currentColor); }
          [part='application-link']:disabled { cursor: not-allowed; opacity: .58; }
          [part='menu-trigger'] { cursor: pointer; padding-inline: .55rem; }
          [part='menu'] { background: var(--system-navigation-menu-background, Canvas); border: 1px solid var(--system-navigation-border-color, currentColor); border-radius: .6rem; box-shadow: 0 .5rem 1.5rem rgba(0,0,0,.2); display: grid; gap: .15rem; left: 0; min-width: 12rem; padding: .3rem; position: absolute; top: calc(100% + .25rem); z-index: 100; }
          [part='menu'] a { border-radius: .4rem; white-space: nowrap; }
          [part='application-state'] { color: var(--system-navigation-muted-color, inherit); font-size: .72rem; max-width: 12rem; }
          [part='status'] { color: var(--system-navigation-muted-color, inherit); font-size: var(--system-navigation-status-font-size, .76rem); margin: 0; padding: .25rem .4rem; }
          [part='retry'] { background: var(--system-navigation-button-background, transparent); cursor: pointer; }
          [hidden] { display: none !important; }
        `;
        this._navigation = document.createElement('nav');
        this._navigation.setAttribute('part', 'navigation');
        this._navigation.setAttribute('aria-label', 'Site navigation');
        this._home = this._link('/', 'Home', 'home-link');
        this._control = this._link(CONTROL_CENTER_PATH, 'Control center', 'control-link');
        this._systemPages = document.createElement('span');
        this._systemPages.setAttribute('part', 'system-pages');
        this._systemPages.setAttribute('role', 'group');
        this._systemPages.setAttribute('aria-label', 'System pages');
        this._systemPages.append(this._home, this._control);
        this._applicationList = document.createElement('span');
        this._applicationList.setAttribute('part', 'applications');
        this._applicationList.setAttribute('role', 'group');
        this._applicationList.setAttribute('aria-label', 'Applications');
        this._status = document.createElement('p');
        this._status.setAttribute('part', 'status');
        this._status.setAttribute('role', 'status');
        this._status.setAttribute('aria-live', 'polite');
        this._retry = document.createElement('button');
        this._retry.type = 'button';
        this._retry.textContent = 'Retry';
        this._retry.hidden = true;
        this._retry.setAttribute('part', 'retry');
        this._retry.addEventListener('click', () => this._loadApplications());
        this._navigation.append(this._systemPages, this._applicationList, this._status, this._retry);
        this.shadowRoot.append(style, this._navigation);
      }

      _link(href, label, part) {
        const link = document.createElement('a');
        link.href = href;
        link.textContent = label;
        link.setAttribute('part', part);
        return link;
      }

      async _loadApplications() {
        if (this._request) this._request.abort();
        const request = new AbortController();
        this._request = request;
        this._applications = [];
        this._applicationList.replaceChildren();
        this._retry.hidden = true;
        this._status.textContent = 'Loading applications…';
        this._emit('system-progress', {phase: 'loading'});
        try {
          const result = await this._client.discoverAllApplications({signal: request.signal});
          if (request.signal.aborted || !this._connected) return;
          const applications = result.applications;
          this._applications = applications;
          this._renderApplications();
          this._status.textContent = applications.length === 0
            ? 'No applications registered.'
            : `${applications.length} application${applications.length === 1 ? '' : 's'}`;
          this._emit('system-progress', {phase: 'ready', applicationCount: applications.length,
            pageCount: result.pageCount, resolutionFingerprints: result.resolutionFingerprints});
        } catch (error) {
          if (request.signal.aborted) return;
          this._applications = [];
          this._applicationList.replaceChildren();
          this._status.textContent = 'Applications are unavailable.';
          this._retry.hidden = false;
          this._updateCurrent();
          this._emit('system-error', {code: 'APPLICATION_DISCOVERY_UNAVAILABLE'});
        } finally {
          if (this._request === request) this._request = null;
        }
      }

      _renderApplications() {
        const fragment = document.createDocumentFragment();
        for (const application of this._applications) {
          const item = document.createElement('application-navigation');
          item.application = application;
          item.setAttribute('current-path', window.location.pathname);
          const selected = this._selectedApplication();
          if (selected) item.setAttribute('selected', selected);
          fragment.append(item);
        }
        this._applicationList.replaceChildren(fragment);
        this._updateCurrent();
      }

      _updateCurrent() {
        const selectedApplication = this._selectedApplication();
        this._home.removeAttribute('aria-current');
        this._control.removeAttribute('aria-current');
        const path = this._routePath(window.location.pathname);
        for (const navigation of this._applicationList.querySelectorAll('application-navigation')) {
          navigation.setAttribute('current-path', window.location.pathname);
          if (selectedApplication) navigation.setAttribute('selected', selectedApplication);
          else navigation.removeAttribute('selected');
        }
        if (path === '/' || path === '/ui/home' || path === '/ui/home/index.html') {
          this._home.setAttribute('aria-current', 'page');
        } else if (path === this._routePath(CONTROL_CENTER_PATH) || path === '/ui/control-center') {
          this._control.setAttribute('aria-current', 'page');
        }
      }

      _routePath(path) {
        const value = path.replace(/\/+$/, '') || '/';
        return value.endsWith('/index.html') ? value.slice(0, -'/index.html'.length) || '/' : value;
      }

      _selectedApplication() {
        const declared = this.getAttribute('application-id');
        if (declared && validSystemIdentifier(declared)) return declared;
        if (window.location.pathname.replace(/\/+$/, '') !== CONTROL_CENTER_PATH) return null;
        if (!window.location.hash.startsWith('#/applications/')) return null;
        const encoded = window.location.hash.slice('#/applications/'.length);
        if (!encoded || encoded.includes('/')) return null;
        try {
          const decoded = decodeURIComponent(encoded);
          return validSystemIdentifier(decoded) ? decoded : null;
        } catch (_) { return null; }
      }

      _emit(name, detail) {
        this.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true}));
      }
    }

    class SystemChat extends HTMLElement {
      constructor() {
        super();
        this._connected = false;
        this._request = null;
        this._conversation = null;
        this.attachShadow({mode: 'open'});
        this._renderShell();
      }

      connectedCallback() {
        if (this._connected) return;
        this._connected = true;
        this._loadConversations();
      }

      disconnectedCallback() {
        this._connected = false;
        if (this._request) this._request.abort();
        this._request = null;
      }

      _renderShell() {
        const style = document.createElement('style');
        style.textContent = `
          :host { display: grid; gap: var(--system-chat-gap, .75rem); color: var(--system-chat-color, inherit); font: inherit; }
          [part='toolbar'] { display: flex; flex-wrap: wrap; gap: .45rem; align-items: center; }
          [part='history'] { min-width: min(100%, 18rem); }
          [part='transcript'] { display: grid; gap: .55rem; max-height: var(--system-chat-transcript-height, 28rem); overflow: auto; }
          [part='message'] { margin: 0; padding: .7rem .8rem; border: 1px solid var(--system-chat-border-color, currentColor); border-radius: var(--system-chat-radius, .75rem); white-space: pre-wrap; }
          [data-role='assistant'] { background: var(--system-chat-assistant-background, rgba(128, 170, 128, .10)); }
          [part='composer'] { display: grid; gap: .5rem; }
          [part='mode-row'] { display: flex; gap: .45rem; align-items: center; }
          textarea { box-sizing: border-box; min-height: 6rem; width: 100%; resize: vertical; font: inherit; padding: .65rem; }
          button, select { font: inherit; padding: .5rem .7rem; }
          button { cursor: pointer; }
          [part='tasks'] { display: grid; gap: .55rem; }
          [part='task'] { border: 1px solid var(--system-chat-border-color, currentColor); border-radius: var(--system-chat-radius, .75rem); padding: .7rem .8rem; }
          [part='task'] h4, [part='task'] p { margin: 0 0 .4rem; }
          [part='task'] ol { margin: .35rem 0 .6rem; padding-left: 1.4rem; }
          [part='task'] pre { max-height: 14rem; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; }
          [part='status'], [part='evidence'] { margin: 0; color: var(--system-chat-muted-color, inherit); font-size: .85rem; }
          [part='evidence'] { overflow-wrap: anywhere; }
          [hidden] { display: none !important; }
        `;
        const toolbar = document.createElement('div');
        toolbar.setAttribute('part', 'toolbar');
        this._history = document.createElement('select');
        this._history.setAttribute('part', 'history');
        this._history.setAttribute('aria-label', 'System conversations');
        this._history.addEventListener('change', () => this._open(this._history.value));
        this._new = document.createElement('button');
        this._new.type = 'button';
        this._new.textContent = 'New conversation';
        this._new.setAttribute('part', 'new-conversation');
        this._new.addEventListener('click', () => {
          this._conversation = null;
          this._history.value = '';
          this._transcript.replaceChildren();
          this._tasks.replaceChildren();
          this._evidence.textContent = '';
          this._setStatus('Ready for a new read-only system question.');
          this._message.focus();
        });
        toolbar.append(this._history, this._new);
        this._transcript = document.createElement('div');
        this._transcript.setAttribute('part', 'transcript');
        this._transcript.setAttribute('role', 'log');
        this._transcript.setAttribute('aria-live', 'polite');
        const form = document.createElement('form');
        form.setAttribute('part', 'composer');
        const modeRow = document.createElement('div');
        modeRow.setAttribute('part', 'mode-row');
        const modeLabel = document.createElement('label');
        modeLabel.textContent = 'Use as ';
        this._mode = document.createElement('select');
        this._mode.setAttribute('aria-label', 'System chat mode');
        for (const [value, label] of [['ask', 'Ask'], ['task', 'Plan task']]) {
          const option = document.createElement('option');
          option.value = value;
          option.textContent = label;
          this._mode.append(option);
        }
        this._mode.addEventListener('change', () => this._updateMode());
        modeLabel.append(this._mode);
        modeRow.append(modeLabel);
        this._message = document.createElement('textarea');
        this._message.maxLength = 8000;
        this._message.required = true;
        this._message.setAttribute('aria-label', 'Ask about the system');
        this._message.placeholder = 'Ask what the system knows or how a registered read contract works…';
        this._send = document.createElement('button');
        this._send.type = 'submit';
        this._send.textContent = 'Ask';
        this._send.setAttribute('part', 'send');
        form.append(modeRow, this._message, this._send);
        form.addEventListener('submit', event => {
          event.preventDefault();
          this._submit();
        });
        this._status = document.createElement('p');
        this._status.setAttribute('part', 'status');
        this._status.setAttribute('role', 'status');
        this._evidence = document.createElement('p');
        this._evidence.setAttribute('part', 'evidence');
        this._tasks = document.createElement('section');
        this._tasks.setAttribute('part', 'tasks');
        this._tasks.setAttribute('aria-label', 'System tasks');
        this.shadowRoot.append(style, toolbar, this._transcript, form, this._status, this._evidence, this._tasks);
        this._updateMode();
      }

      async _loadConversations() {
        this._setStatus('Loading system conversations…');
        try {
          const page = await this._requestJson('/api/control/system/conversations?limit=25');
          if (!page || !Array.isArray(page.items) || page.items.length > 25) throw new Error('invalid-page');
          const option = document.createElement('option');
          option.value = '';
          option.textContent = 'New conversation';
          const fragment = document.createDocumentFragment();
          fragment.append(option);
          for (const item of page.items) {
            if (!this._validSummary(item)) throw new Error('invalid-summary');
            const choice = document.createElement('option');
            choice.value = item.id;
            choice.textContent = item.title;
            fragment.append(choice);
          }
          if (!this._connected) return;
          this._history.replaceChildren(fragment);
          if (page.items.length > 0) await this._open(page.items[0].id);
          else this._setStatus('Ready for a new read-only system question.');
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._setStatus('System conversations are unavailable.');
          this._emit('system-error', {code: 'SYSTEM_CHAT_UNAVAILABLE'});
        }
      }

      async _open(id) {
        if (!id) {
          this._conversation = null;
          this._transcript.replaceChildren();
          this._tasks.replaceChildren();
          this._evidence.textContent = '';
          this._setStatus('Ready for a new read-only system question.');
          return;
        }
        this._setBusy(true, 'Loading conversation…');
        try {
          const document = await this._requestJson('/api/control/system/conversations/' + encodeURIComponent(id));
          this._accept(document);
          await this._loadTasks();
          this._emit('system-progress', {phase: 'ready', conversationId: id});
        } catch (error) {
          if (error.name !== 'AbortError') {
            this._setStatus('The system conversation could not be loaded.');
            this._emit('system-error', {code: 'SYSTEM_CHAT_LOAD_FAILED'});
          }
        } finally { this._setBusy(false); }
      }

      async _submit() {
        const message = this._message.value.trim();
        if (!message || message.length > 8000 || this._send.disabled) return;
        if (this._mode.value === 'task') {
          await this._submitTask(message);
          return;
        }
        this._setBusy(true, 'The local system assistant is reading bounded system context…');
        this._emit('system-progress', {phase: 'working'});
        try {
          const key = 'system-chat.' + this._randomId();
          const path = this._conversation
            ? '/api/control/system/conversations/' + encodeURIComponent(this._conversation.summary.id) + '/turns'
            : '/api/control/system/conversations';
          const body = this._conversation
            ? {expectedRevision: this._conversation.summary.revision, message, idempotencyKey: key}
            : {message, idempotencyKey: key};
          const document = await this._requestJson(path, body);
          this._message.value = '';
          this._accept(document);
          await this._refreshHistory();
          await this._loadTasks();
          this._emit('system-progress', {phase: 'complete', conversationId: document.summary.id});
        } catch (error) {
          if (error.name !== 'AbortError') {
            this._setStatus(error.message || 'The system question failed.');
            this._emit('system-error', {code: 'SYSTEM_CHAT_REQUEST_FAILED'});
          }
        } finally { this._setBusy(false); }
      }

      async _submitTask(intent) {
        if (!this._conversation) {
          this._setStatus('Choose an existing system conversation or ask one question before planning a task.');
          return;
        }
        this._setBusy(true, 'The local planner is preparing an inert, reviewable task…');
        this._emit('system-progress', {phase: 'planning-task'});
        try {
          const task = await this._requestJson(
            '/api/control/system/conversations/' + encodeURIComponent(this._conversation.summary.id) + '/tasks',
            {operation: 'resolve', intent, agenda: null, idempotencyKey: 'system-task.' + this._randomId()});
          this._message.value = '';
          await this._loadTasks();
          this._setStatus(task.summary.status === 'prepared'
            ? 'Task prepared. Review every step before confirming and running it.'
            : (task.summary.safeSummary || `Task ended ${task.summary.status}.`));
          this._emit('system-proposal', {taskId: task.summary.id,
            planFingerprint: task.summary.planFingerprint, status: task.summary.status});
          this._emit('system-progress', {phase: 'task-prepared', taskId: task.summary.id});
        } catch (error) {
          if (error.name !== 'AbortError') {
            this._setStatus(error.message || 'The system task could not be prepared.');
            this._emit('system-error', {code: 'SYSTEM_TASK_PREPARE_FAILED'});
          }
        } finally { this._setBusy(false); }
      }

      async _loadTasks() {
        this._tasks.replaceChildren();
        if (!this._conversation) return;
        try {
          const page = await this._requestJson('/api/control/system/conversations/' +
            encodeURIComponent(this._conversation.summary.id) + '/tasks?limit=10');
          if (!page || !Array.isArray(page.items) || page.items.length > 10) throw new Error('invalid-task-page');
          const tasks = [];
          for (const summary of page.items) {
            if (!summary || typeof summary.id !== 'string') throw new Error('invalid-task-summary');
            tasks.push(await this._requestJson('/api/control/system/tasks/' + encodeURIComponent(summary.id)));
          }
          this._renderTasks(tasks);
        } catch (error) {
          if (error.name !== 'AbortError') this._setStatus('System task receipts are unavailable.');
        }
      }

      _renderTasks(tasks) {
        const fragment = document.createDocumentFragment();
        for (const task of tasks) {
          if (!this._validTask(task)) continue;
          const card = document.createElement('article');
          card.setAttribute('part', 'task');
          const title = document.createElement('h4');
          title.textContent = `${task.summary.status}: ${task.summary.intent}`;
          const summary = document.createElement('p');
          summary.textContent = task.summary.safeSummary || 'No additional summary.';
          const fingerprint = document.createElement('p');
          fingerprint.textContent = task.summary.planFingerprint
            ? `Plan fingerprint: ${task.summary.planFingerprint}` : 'No executable plan fingerprint.';
          card.append(title, summary, fingerprint);
          if (task.steps.length) {
            const list = document.createElement('ol');
            for (const step of task.steps) {
              const item = document.createElement('li');
              item.textContent = `${step.mode}: ${step.capabilityId} v${step.capabilityVersion} ` +
                `(${step.owner}, ${step.descriptorFingerprint}) — ${step.safeSummary}`;
              if (step.mode === 'read' && step.result != null) {
                const details = document.createElement('details');
                const label = document.createElement('summary');
                label.textContent = `Read evidence ${step.resultFingerprint}`;
                const result = document.createElement('pre');
                result.textContent = JSON.stringify(step.result, null, 2);
                details.append(label, result);
                item.append(details);
              }
              list.append(item);
            }
            card.append(list);
          }
          const latest = Array.isArray(task.executions) ? task.executions[0] : null;
          if (latest) {
            const receipt = document.createElement('p');
            receipt.textContent = `Receipt: ${latest.status}. ${latest.safeSummary || ''}`;
            card.append(receipt);
            if (Array.isArray(latest.steps)) {
              const receipts = document.createElement('ol');
              for (const step of latest.steps) {
                const item = document.createElement('li');
                item.textContent = `${step.stepId}: ${step.status}` +
                  (step.operationId ? ` — operation ${step.operationId}` : '') +
                  (step.errorMessage ? ` — ${step.errorMessage}` : '');
                receipts.append(item);
              }
              card.append(receipts);
            }
          } else if (task.summary.status === 'prepared') {
            const warning = document.createElement('p');
            warning.textContent = 'Review carefully: if a later step fails, earlier completed steps remain committed.';
            const run = document.createElement('button');
            run.type = 'button';
            run.textContent = 'Confirm and run';
            run.addEventListener('click', () => this._confirmAndRun(task, run));
            card.append(warning, run);
          }
          fragment.append(card);
        }
        this._tasks.replaceChildren(fragment);
      }

      async _confirmAndRun(task, button) {
        if (button.disabled || !task.summary.planFingerprint) return;
        button.disabled = true;
        this._setStatus('Confirming the exact displayed plan for five minutes…');
        try {
          const confirmation = await this._requestJson(
            '/api/control/system/tasks/' + encodeURIComponent(task.summary.id) + '/confirmations',
            {planFingerprint: task.summary.planFingerprint, idempotencyKey: 'system-confirm.' + this._randomId()});
          this._setStatus('Running confirmed steps with a durable receipt for each step…');
          const receipt = await this._requestJson(
            '/api/control/system/tasks/' + encodeURIComponent(task.summary.id) + '/executions',
            {confirmationId: confirmation.id, planFingerprint: task.summary.planFingerprint,
              idempotencyKey: 'system-execute.' + this._randomId()});
          await this._loadTasks();
          this._setStatus(receipt.safeSummary || `Task execution ended ${receipt.status}.`);
          this._emit('system-receipt', {taskId: task.summary.id, receiptId: receipt.id,
            status: receipt.status, stepCount: Array.isArray(receipt.steps) ? receipt.steps.length : 0});
          this._emit('system-progress', {phase: 'task-executed', taskId: task.summary.id, receiptId: receipt.id});
        } catch (error) {
          button.disabled = false;
          this._setStatus(error.message || 'The confirmed task could not be executed.');
          this._emit('system-error', {code: 'SYSTEM_TASK_EXECUTION_FAILED'});
        }
      }

      _validTask(task) {
        return task && task.summary && typeof task.summary.id === 'string' &&
          /^system-task\.[0-9a-f]{32}$/.test(task.summary.id) &&
          typeof task.summary.intent === 'string' && task.summary.intent.length <= 8000 &&
          typeof task.summary.status === 'string' && Array.isArray(task.steps) && task.steps.length <= 12 &&
          task.steps.every(step => step && ['read', 'write'].includes(step.mode) &&
            typeof step.capabilityId === 'string' && step.capabilityId.startsWith('system.') &&
            Number.isInteger(step.capabilityVersion) && step.capabilityVersion >= 1 &&
            typeof step.owner === 'string' && step.owner.length <= 80 &&
            typeof step.descriptorFingerprint === 'string' && /^[0-9A-F]{64}$/.test(step.descriptorFingerprint) &&
            typeof step.safeSummary === 'string');
      }

      _updateMode() {
        const task = this._mode && this._mode.value === 'task';
        this._send.textContent = task ? 'Plan task' : 'Ask';
        this._message.placeholder = task
          ? 'Describe a system change to prepare for review…'
          : 'Ask what the system knows or how a registered read contract works…';
        this._message.setAttribute('aria-label', task ? 'Describe a system task' : 'Ask about the system');
      }

      async _refreshHistory() {
        const page = await this._requestJson('/api/control/system/conversations?limit=25');
        if (!page || !Array.isArray(page.items) || page.items.some(item => !this._validSummary(item))) return;
        const fragment = document.createDocumentFragment();
        const fresh = document.createElement('option');
        fresh.value = '';
        fresh.textContent = 'New conversation';
        fragment.append(fresh);
        for (const item of page.items) {
          const option = document.createElement('option');
          option.value = item.id;
          option.textContent = item.title;
          fragment.append(option);
        }
        this._history.replaceChildren(fragment);
        this._history.value = this._conversation ? this._conversation.summary.id : '';
      }

      _accept(conversation) {
        if (!conversation || !this._validSummary(conversation.summary) ||
            !Array.isArray(conversation.messages) || conversation.messages.length > 1000 ||
            !Array.isArray(conversation.turns) || conversation.turns.length > 500) throw new Error('invalid-conversation');
        this._conversation = conversation;
        this._history.value = conversation.summary.id;
        const fragment = document.createDocumentFragment();
        for (const message of conversation.messages) {
          if (!message || !['user', 'assistant'].includes(message.role) ||
              typeof message.content !== 'string' || message.content.length < 1 || message.content.length > 8000) {
            throw new Error('invalid-message');
          }
          const paragraph = document.createElement('p');
          paragraph.setAttribute('part', 'message');
          paragraph.dataset.role = message.role;
          paragraph.textContent = (message.role === 'assistant' ? 'System: ' : 'You: ') + message.content;
          fragment.append(paragraph);
        }
        this._transcript.replaceChildren(fragment);
        const turn = [...conversation.turns].reverse().find(value => value.context);
        if (turn && this._validContext(turn.context)) {
          this._evidence.textContent = `${turn.context.disposition}. Evidence: ${turn.context.sourceReferences.join(', ') || 'none'}`;
        } else this._evidence.textContent = '';
        const latest = conversation.turns[conversation.turns.length - 1];
        this._setStatus(latest && latest.status !== 'completed'
          ? (latest.errorMessage || `Turn ${latest.status}.`)
          : 'Read-only system answer complete.');
        this._transcript.scrollTop = this._transcript.scrollHeight;
      }

      _validSummary(value) {
        return value && typeof value.id === 'string' && value.id.length === 45 &&
          value.provider === 'local' && value.scope === 'system' &&
          typeof value.title === 'string' && value.title.length >= 1 && value.title.length <= 120 &&
          Number.isInteger(value.revision) && value.revision >= 1;
      }

      _validContext(value) {
        return value && value.profile === 'system-read-v1' &&
          typeof value.fingerprint === 'string' && /^[0-9A-F]{64}$/.test(value.fingerprint) &&
          ['answered', 'unknown', 'unsupported', 'needs-input', 'needs-application', 'unavailable'].includes(value.disposition) &&
          Array.isArray(value.sourceReferences) && value.sourceReferences.length <= 24 &&
          value.sourceReferences.every(reference => typeof reference === 'string' && reference.length <= 320);
      }

      async _requestJson(path, body) {
        if (this._request) this._request.abort();
        const request = new AbortController();
        this._request = request;
        try {
          return await systemWebClient.requestJson(path, {
            method: body ? 'POST' : 'GET',
            body: body || undefined,
            signal: request.signal
          });
        } finally {
          if (this._request === request) this._request = null;
        }
      }

      _randomId() {
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
      }

      _setBusy(value, message) {
        this._send.disabled = value;
        this._message.disabled = value;
        this._history.disabled = value;
        this._new.disabled = value;
        this._mode.disabled = value;
        if (message) this._setStatus(message);
      }

      _setStatus(message) { this._status.textContent = message; }

      _emit(name, detail) {
        this.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true}));
      }
    }

    const SYSTEM_CAPABILITY_ENDPOINT = '/api/control/system/capabilities/';
    const SYSTEM_CONVERSATIONS_ENDPOINT = '/api/control/system/conversations?limit=1';
    const SYSTEM_COMPONENT_MAXIMUM_INPUT_BYTES = 96 * 1024;
    const SYSTEM_COMPONENT_MAXIMUM_SCHEMA_BYTES = 96 * 1024;
    const SYSTEM_COMPONENT_TASK_STATUSES = ['prepared', 'completed', 'needs-input', 'unknown', 'unsupported', 'unavailable', 'failed'];
    const SYSTEM_COMPONENT_EXECUTION_STATUSES = ['succeeded', 'partial', 'failed', 'stale', 'unauthorized', 'cancelled', 'timed-out', 'indeterminate'];

    class SystemComponentError extends Error {
      constructor(code, message) {
        super(message);
        this.name = 'SystemComponentError';
        this.code = /^[A-Z0-9_]{1,100}$/.test(code || '') ? code : 'SYSTEM_COMPONENT_UNAVAILABLE';
      }
    }

    function systemComponentRandomId() {
      const bytes = new Uint8Array(16);
      crypto.getRandomValues(bytes);
      return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
    }

    function systemComponentCapabilityId(value) {
      return typeof value === 'string' && value.length >= 8 && value.length <= 120 &&
        /^system\.[a-z0-9.-]+$/.test(value);
    }

    function systemComponentClone(value) {
      let nodes = 0;
      const visit = (item, depth) => {
        nodes += 1;
        if (nodes > 4096 || depth > 24) throw new SystemComponentError(
          'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input is too complex.');
        if (item === null || typeof item === 'string' || typeof item === 'boolean') return item;
        if (typeof item === 'number') {
          if (!Number.isFinite(item)) throw new SystemComponentError(
            'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input contains a non-finite number.');
          return item;
        }
        if (Array.isArray(item)) {
          if (item.length > 1024) throw new SystemComponentError(
            'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input contains too many values.');
          return item.map(entry => visit(entry, depth + 1));
        }
        if (typeof item !== 'object' || ![Object.prototype, null].includes(Object.getPrototypeOf(item))) {
          throw new SystemComponentError(
            'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input must contain only plain JSON values.');
        }
        const result = Object.create(null);
        const entries = Object.entries(item);
        if (entries.length > 512) throw new SystemComponentError(
          'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input contains too many fields.');
        for (const [key, entry] of entries) {
          if (!key || key.length > 200 || ['__proto__', 'prototype', 'constructor'].includes(key)) {
            throw new SystemComponentError(
              'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared input contains an invalid field.');
          }
          result[key] = visit(entry, depth + 1);
        }
        return result;
      };
      const clone = visit(value, 0);
      if (clone === null || Array.isArray(clone) || typeof clone !== 'object') throw new SystemComponentError(
        'SYSTEM_COMPONENT_INPUT_INVALID', 'The declared capability input must be one JSON object.');
      const json = JSON.stringify(clone);
      if (new TextEncoder().encode(json).length > SYSTEM_COMPONENT_MAXIMUM_INPUT_BYTES) {
        throw new SystemComponentError(
          'SYSTEM_COMPONENT_INPUT_TOO_LARGE', 'The declared input exceeds 96 KiB.');
      }
      return JSON.parse(json);
    }

    async function systemComponentRequest(path, body, signal) {
      try {
        return await systemWebClient.requestJson(path, {
          method: body === undefined ? 'GET' : 'POST',
          body,
          signal
        });
      } catch (error) {
        throw new SystemComponentError(error?.code, error?.message);
      }
    }

    function systemComponentDescriptor(value, capabilityId) {
      if (!value || value.id !== capabilityId || !systemComponentCapabilityId(value.id) ||
          !Number.isInteger(value.version) || value.version < 1 ||
          typeof value.fingerprint !== 'string' || !/^[0-9A-F]{64}$/.test(value.fingerprint) ||
          typeof value.owner !== 'string' || value.owner.length < 1 || value.owner.length > 80 ||
          typeof value.description !== 'string' || value.description.length < 1 || value.description.length > 500 ||
          !['read', 'write'].includes(value.mode) ||
          !value.inputSchema || Array.isArray(value.inputSchema) || typeof value.inputSchema !== 'object' ||
          typeof value.inputSchemaHash !== 'string' || !/^[0-9A-F]{64}$/.test(value.inputSchemaHash) ||
          !Array.isArray(value.procedureIds) || value.procedureIds.length < 1 || value.procedureIds.length > 16 ||
          value.procedureIds.some(id => typeof id !== 'string' || id.length < 1 || id.length > 200) ||
          typeof value.requiresConfirmation !== 'boolean' || typeof value.requiresIdempotencyKey !== 'boolean' ||
          (value.mode === 'write' && (!value.requiresConfirmation || !value.requiresIdempotencyKey)) ||
          (value.mode === 'read' && (value.requiresConfirmation || value.requiresIdempotencyKey))) {
        throw new SystemComponentError(
          'SYSTEM_CAPABILITY_DESCRIPTOR_INVALID', 'The system capability contract is invalid.');
      }
      if (new TextEncoder().encode(JSON.stringify(value.inputSchema)).length > SYSTEM_COMPONENT_MAXIMUM_SCHEMA_BYTES) {
        throw new SystemComponentError(
          'SYSTEM_CAPABILITY_SCHEMA_TOO_LARGE', 'The system capability input contract is too large.');
      }
      return value;
    }

    async function systemComponentConversation(signal) {
      const page = await systemComponentRequest(SYSTEM_CONVERSATIONS_ENDPOINT, undefined, signal);
      if (!page || !Array.isArray(page.items) || page.items.length > 1 ||
          page.items.some(item => !item || typeof item.id !== 'string' ||
            !/^conversation\.[0-9a-f]{32}$/.test(item.id) || item.scope !== 'system' || item.provider !== 'local')) {
        throw new SystemComponentError(
          'SYSTEM_ACTION_CONVERSATION_INVALID', 'System conversation discovery returned invalid data.');
      }
      if (page.items.length === 0) throw new SystemComponentError(
        'SYSTEM_ACTION_CONVERSATION_REQUIRED', 'Start a system chat before using a system action or form.');
      return page.items[0].id;
    }

    function systemComponentTask(value, descriptor) {
      if (!value || !value.summary || typeof value.summary.id !== 'string' ||
          !/^system-task\.[0-9a-f]{32}$/.test(value.summary.id) ||
          !SYSTEM_COMPONENT_TASK_STATUSES.includes(value.summary.status) ||
          typeof value.summary.safeSummary !== 'string' || value.summary.safeSummary.length > 1000 ||
          typeof value.summary.planFingerprint !== 'string' ||
          (value.summary.planFingerprint && !/^[0-9A-F]{64}$/.test(value.summary.planFingerprint)) ||
          !Array.isArray(value.steps) || value.steps.length > 1 ||
          !Array.isArray(value.executions) ||
          typeof value.errorCode !== 'string' || value.errorCode.length > 160 ||
          typeof value.errorMessage !== 'string' || value.errorMessage.length > 1000) {
        throw new SystemComponentError('SYSTEM_TASK_RESPONSE_INVALID', 'The system task response is invalid.');
      }
      if (value.steps.length === 0) {
        if (['prepared', 'completed'].includes(value.summary.status)) {
          throw new SystemComponentError('SYSTEM_TASK_RESPONSE_INVALID', 'The system task response has no reviewed step.');
        }
        return value;
      }
      const step = value.steps[0];
      if (!step || step.capabilityId !== descriptor.id || step.capabilityVersion !== descriptor.version ||
          step.descriptorFingerprint !== descriptor.fingerprint || step.mode !== descriptor.mode ||
          typeof step.owner !== 'string' || step.owner.length > 80 ||
          typeof step.safeSummary !== 'string' || step.safeSummary.length > 1000 ||
          !Array.isArray(step.affectedReferences) || step.affectedReferences.length > 64 ||
          step.affectedReferences.some(reference => typeof reference !== 'string' || reference.length > 320)) {
        throw new SystemComponentError(
          'SYSTEM_CAPABILITY_CHANGED', 'The system capability changed. Reload it before trying again.');
      }
      if (descriptor.mode === 'write' && value.summary.status === 'prepared' &&
          !/^[0-9A-F]{64}$/.test(value.summary.planFingerprint)) {
        throw new SystemComponentError('SYSTEM_TASK_RESPONSE_INVALID', 'The prepared task has no exact plan fingerprint.');
      }
      return value;
    }

    function systemComponentExecution(value, task) {
      if (!value || typeof value.id !== 'string' || value.id.length > 80 ||
          !SYSTEM_COMPONENT_EXECUTION_STATUSES.includes(value.status) ||
          value.planFingerprint !== task.summary.planFingerprint ||
          typeof value.safeSummary !== 'string' || value.safeSummary.length > 1000 ||
          !Array.isArray(value.steps) || value.steps.length > 12) {
        throw new SystemComponentError(
          'SYSTEM_TASK_RECEIPT_INVALID', 'The system task receipt is invalid.');
      }
      return value;
    }

    function systemComponentParagraph(text, part) {
      const value = document.createElement('p');
      value.textContent = text;
      if (part) value.setAttribute('part', part);
      return value;
    }

    function systemComponentTaskView(task, confirm) {
      const card = document.createElement('article');
      card.setAttribute('part', 'proposal');
      const heading = document.createElement('h3');
      heading.textContent = task.summary.status === 'prepared' ? 'Review system change' : 'System result';
      const summary = systemComponentParagraph(task.summary.safeSummary || `Task ended ${task.summary.status}.`, 'summary');
      const step = task.steps[0];
      if (!step) {
        heading.textContent = 'System request not completed';
        card.append(heading, summary);
        if (task.errorMessage) card.append(systemComponentParagraph(task.errorMessage, 'error'));
        card.append(systemComponentParagraph(`Task reference: ${task.summary.id}`, 'task-reference'));
        return card;
      }
      const contract = systemComponentParagraph(
        `${step.capabilityId} v${step.capabilityVersion} · ${step.owner} · ${step.descriptorFingerprint}`,
        'contract');
      const input = document.createElement('details');
      input.setAttribute('part', 'input-details');
      const inputLabel = document.createElement('summary');
      inputLabel.textContent = 'Reviewed input';
      const inputValue = document.createElement('pre');
      inputValue.textContent = JSON.stringify(step.input, null, 2);
      input.append(inputLabel, inputValue);
      card.append(heading, summary, contract, input);
      if (step.affectedReferences.length) {
        const affected = document.createElement('ul');
        affected.setAttribute('part', 'affected');
        for (const reference of step.affectedReferences) {
          const item = document.createElement('li');
          item.textContent = reference;
          affected.append(item);
        }
        const label = systemComponentParagraph('Affected system references:', 'affected-label');
        card.append(label, affected);
      }
      if (step.mode === 'read') {
        const result = document.createElement('pre');
        result.setAttribute('part', 'result');
        result.textContent = step.result == null ? 'No result was returned.' : JSON.stringify(step.result, null, 2);
        card.append(result);
      } else if (task.summary.status === 'prepared') {
        const fingerprint = systemComponentParagraph(
          `Plan fingerprint: ${task.summary.planFingerprint}`, 'fingerprint');
        const warning = systemComponentParagraph(
          'If a later step fails, earlier completed steps remain committed.', 'warning');
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = 'Confirm and run';
        button.setAttribute('part', 'confirm');
        button.addEventListener('click', () => confirm(button));
        card.append(fingerprint, warning, button);
      }
      return card;
    }

    function systemComponentReceiptView(receipt) {
      const card = document.createElement('article');
      card.setAttribute('part', 'receipt');
      const heading = document.createElement('h3');
      heading.textContent = `System receipt: ${receipt.status}`;
      card.append(heading, systemComponentParagraph(receipt.safeSummary || 'No additional receipt summary.', 'summary'));
      if (receipt.errorMessage) card.append(systemComponentParagraph(receipt.errorMessage, 'error'));
      if (receipt.steps.length) {
        const list = document.createElement('ol');
        list.setAttribute('part', 'receipt-steps');
        for (const step of receipt.steps) {
          const item = document.createElement('li');
          item.textContent = `${step.stepId}: ${step.status}` +
            (step.operationId ? ` · operation ${step.operationId}` : '') +
            (step.errorMessage ? ` · ${step.errorMessage}` : '');
          if (step.output != null) {
            const output = document.createElement('pre');
            output.textContent = JSON.stringify(step.output, null, 2);
            item.append(output);
          }
          list.append(item);
        }
        card.append(list);
      }
      return card;
    }

    class SystemInteractionElement extends HTMLElement {
      constructor() {
        super();
        this._connected = false;
        this._descriptor = null;
        this._descriptorRequest = null;
        this._operationRequest = null;
        this._busy = false;
        this.attachShadow({mode: 'open'});
      }

      _connect() {
        if (this._connected) return;
        this._connected = true;
        this._loadDescriptor();
      }

      _disconnect() {
        this._connected = false;
        if (this._descriptorRequest) this._descriptorRequest.abort();
        if (this._operationRequest) this._operationRequest.abort();
        this._descriptorRequest = null;
        this._operationRequest = null;
      }

      _capabilityChanged() {
        if (!this._connected) return;
        if (this._descriptorRequest) this._descriptorRequest.abort();
        if (this._operationRequest) this._operationRequest.abort();
        this._descriptor = null;
        this._busy = false;
        this._clearResult();
        this._loadDescriptor();
      }

      async _loadDescriptor() {
        const capabilityId = (this.getAttribute('capability-id') || '').trim();
        this._setReady(false);
        this._clearResult();
        if (!systemComponentCapabilityId(capabilityId)) {
          this._setStatus('Set one valid system capability before using this control.', true);
          this._emit('system-error', {code: 'SYSTEM_CAPABILITY_ID_INVALID'});
          return;
        }
        const request = new AbortController();
        this._descriptorRequest = request;
        this._setStatus('Loading the current system contract…');
        this._emit('system-progress', {phase: 'loading-capability', capabilityId});
        try {
          const value = await systemComponentRequest(
            SYSTEM_CAPABILITY_ENDPOINT + encodeURIComponent(capabilityId), undefined, request.signal);
          const descriptor = systemComponentDescriptor(value, capabilityId);
          if (!this._connected || (this.getAttribute('capability-id') || '').trim() !== capabilityId) return;
          this._descriptor = descriptor;
          this._renderDescriptor(descriptor);
          this._setReady(true);
          this._setStatus(`Ready: ${descriptor.description}`);
          this._emit('system-progress', {phase: 'capability-ready', capabilityId,
            version: descriptor.version, descriptorFingerprint: descriptor.fingerprint});
        } catch (error) {
          if (error.name === 'AbortError') return;
          this._setStatus(error.message || 'The system contract is unavailable.', true);
          this._emit('system-error', {code: error.code || 'SYSTEM_CAPABILITY_UNAVAILABLE'});
        } finally {
          if (this._descriptorRequest === request) this._descriptorRequest = null;
        }
      }

      async _prepare(input) {
        if (this._busy || !this._descriptor) return;
        const descriptor = this._descriptor;
        let declared;
        try { declared = systemComponentClone(input); }
        catch (error) {
          this._setStatus(error.message, true);
          this._emit('system-error', {code: error.code});
          return;
        }
        const request = new AbortController();
        this._operationRequest = request;
        this._setBusy(true);
        this._clearResult();
        this._setStatus('Preparing a reviewable system task…');
        this._emit('system-progress', {phase: 'preparing', capabilityId: descriptor.id});
        try {
          const conversationId = await systemComponentConversation(request.signal);
          const task = systemComponentTask(await systemComponentRequest(
            '/api/control/system/conversations/' + encodeURIComponent(conversationId) + '/tasks',
            {operation: 'submit', intent: `Use ${descriptor.id} with the supplied reviewed values.`,
              agenda: [{capabilityId: descriptor.id, input: declared}],
              idempotencyKey: 'system-component.' + systemComponentRandomId()}, request.signal), descriptor);
          this._showTask(task);
          if (task.summary.status === 'prepared') {
            this._setStatus('Review the exact proposal before confirming it.');
            this._emit('system-proposal', {taskId: task.summary.id,
              capabilityId: descriptor.id, planFingerprint: task.summary.planFingerprint,
              status: task.summary.status});
            this._emit('system-progress', {phase: 'prepared', taskId: task.summary.id});
          } else if (task.summary.status === 'completed' && descriptor.mode === 'read') {
            this._setStatus(task.summary.safeSummary || 'The system read completed.');
            this._emit('system-receipt', {taskId: task.summary.id, receiptId: null,
              status: 'completed', stepCount: 1});
            this._emit('system-progress', {phase: 'complete', taskId: task.summary.id});
          } else {
            this._setStatus(task.errorMessage || task.summary.safeSummary ||
              `The task ended ${task.summary.status}.`, true);
            this._emit('system-error', {code: task.errorCode || 'SYSTEM_TASK_NOT_PREPARED'});
          }
        } catch (error) {
          if (error.name !== 'AbortError') {
            this._setStatus(error.message || 'The system task could not be prepared.', true);
            this._emit('system-error', {code: error.code || 'SYSTEM_TASK_PREPARE_FAILED'});
          }
        } finally {
          if (this._operationRequest === request) this._operationRequest = null;
          this._setBusy(false);
        }
      }

      _showTask(task) {
        this._result.replaceChildren(systemComponentTaskView(task,
          button => this._confirmAndRun(task, button)));
      }

      async _confirmAndRun(task, button) {
        if (this._busy || !this._descriptor || !task.summary.planFingerprint) return;
        const request = new AbortController();
        this._operationRequest = request;
        this._setBusy(true);
        button.disabled = true;
        const confirmationKey = 'system-component-confirm.' + systemComponentRandomId();
        const executionKey = 'system-component-execute.' + systemComponentRandomId();
        this._setStatus('Confirming the exact displayed plan for five minutes…');
        this._emit('system-progress', {phase: 'confirming', taskId: task.summary.id});
        try {
          const confirmation = await systemComponentRequest(
            '/api/control/system/tasks/' + encodeURIComponent(task.summary.id) + '/confirmations',
            {planFingerprint: task.summary.planFingerprint, idempotencyKey: confirmationKey}, request.signal);
          if (!confirmation || typeof confirmation.id !== 'string' || confirmation.id.length > 80 ||
              confirmation.planFingerprint !== task.summary.planFingerprint) {
            throw new SystemComponentError(
              'SYSTEM_TASK_CONFIRMATION_INVALID', 'The system confirmation response is invalid.');
          }
          this._setStatus('Running the confirmed task with durable receipts…');
          this._emit('system-progress', {phase: 'executing', taskId: task.summary.id});
          const receipt = systemComponentExecution(await systemComponentRequest(
            '/api/control/system/tasks/' + encodeURIComponent(task.summary.id) + '/executions',
            {confirmationId: confirmation.id, planFingerprint: task.summary.planFingerprint,
              idempotencyKey: executionKey}, request.signal), task);
          this._showReceipt(task, receipt);
        } catch (error) {
          if (error.name !== 'AbortError') {
            const recovered = await this._recoverReceipt(task).catch(() => false);
            if (!recovered) {
              button.disabled = false;
              this._setStatus(error.message || 'The confirmed task could not be executed.', true);
              this._emit('system-error', {code: error.code || 'SYSTEM_TASK_EXECUTION_FAILED'});
            }
          }
        } finally {
          if (this._operationRequest === request) this._operationRequest = null;
          this._setBusy(false);
        }
      }

      async _recoverReceipt(task) {
        const current = systemComponentTask(await systemComponentRequest(
          '/api/control/system/tasks/' + encodeURIComponent(task.summary.id), undefined, undefined), this._descriptor);
        if (!current.executions.length) return false;
        this._showReceipt(task, systemComponentExecution(current.executions[0], task));
        return true;
      }

      _showReceipt(task, receipt) {
        this._result.replaceChildren(systemComponentReceiptView(receipt));
        this._setStatus(receipt.safeSummary || `System execution ended ${receipt.status}.`,
          receipt.status !== 'succeeded');
        this._emit('system-receipt', {taskId: task.summary.id, receiptId: receipt.id,
          status: receipt.status, stepCount: receipt.steps.length});
        this._emit('system-progress', {phase: 'complete', taskId: task.summary.id, receiptId: receipt.id});
      }

      _setBusy(value) {
        this._busy = value;
        this._setReady(!value && !!this._descriptor);
      }

      _setStatus(message, error = false) {
        this._status.textContent = message;
        this._status.setAttribute('role', error ? 'alert' : 'status');
      }

      _clearResult() { if (this._result) this._result.replaceChildren(); }

      _emit(name, detail) {
        this.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true}));
      }
    }

    class SystemActionButton extends SystemInteractionElement {
      static get observedAttributes() { return ['capability-id', 'input-json']; }

      constructor() {
        super();
        this._propertyInput = undefined;
        const style = document.createElement('style');
        style.textContent = `
          :host { display: grid; gap: var(--system-action-gap, .55rem); color: var(--system-action-color, inherit); font: inherit; }
          button { justify-self: start; cursor: pointer; border: 1px solid var(--system-action-border-color, currentColor); border-radius: var(--system-action-radius, .55rem); background: var(--system-action-background, transparent); color: inherit; font: inherit; padding: var(--system-action-padding, .55rem .8rem); }
          button:disabled { cursor: not-allowed; opacity: .65; }
          [part='status'], [part='summary'], [part='contract'], [part='affected-label'], [part='warning'], [part='fingerprint'], [part='error'] { margin: 0; overflow-wrap: anywhere; }
          [part='status'], [part='contract'] { font-size: .85rem; }
          [part='proposal'], [part='receipt'] { display: grid; gap: .5rem; border: 1px solid var(--system-action-border-color, currentColor); border-radius: var(--system-action-radius, .55rem); padding: .75rem; }
          [part='proposal'] h3, [part='receipt'] h3 { margin: 0; font-size: 1rem; }
          pre { max-height: 15rem; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; }
        `;
        this._button = document.createElement('button');
        this._button.type = 'button';
        this._button.disabled = true;
        this._button.setAttribute('part', 'button');
        this._button.addEventListener('click', () => this._activate());
        this._status = document.createElement('p');
        this._status.setAttribute('part', 'status');
        this._status.setAttribute('aria-live', 'polite');
        this._result = document.createElement('section');
        this._result.setAttribute('part', 'result-area');
        this._result.setAttribute('aria-label', 'System action result');
        this.shadowRoot.append(style, this._button, this._status, this._result);
      }

      connectedCallback() { this._connect(); }
      disconnectedCallback() { this._disconnect(); }

      attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue === newValue) return;
        if (name === 'input-json') {
          this._propertyInput = undefined;
          this._clearResult();
          return;
        }
        this._capabilityChanged();
      }

      get input() {
        if (this._propertyInput !== undefined) return systemComponentClone(this._propertyInput);
        return this._attributeInput();
      }

      set input(value) {
        this._propertyInput = systemComponentClone(value);
        this._clearResult();
      }

      _attributeInput() {
        const value = this.getAttribute('input-json');
        if (value === null || value.trim() === '') return {};
        if (new TextEncoder().encode(value).length > SYSTEM_COMPONENT_MAXIMUM_INPUT_BYTES) {
          throw new SystemComponentError(
            'SYSTEM_COMPONENT_INPUT_TOO_LARGE', 'The declared input exceeds 96 KiB.');
        }
        try { return systemComponentClone(JSON.parse(value)); }
        catch (error) {
          if (error instanceof SystemComponentError) throw error;
          throw new SystemComponentError(
            'SYSTEM_COMPONENT_INPUT_INVALID', 'The input-json attribute is not one JSON object.');
        }
      }

      _activate() {
        try { this._prepare(this.input); }
        catch (error) {
          this._setStatus(error.message, true);
          this._emit('system-error', {code: error.code || 'SYSTEM_COMPONENT_INPUT_INVALID'});
        }
      }

      _renderDescriptor(descriptor) {
        this._button.textContent = this.textContent.trim() || `Use ${descriptor.id}`;
      }

      _setReady(value) { this._button.disabled = !value; }
    }

    class SystemForm extends SystemInteractionElement {
      static get observedAttributes() { return ['capability-id']; }

      constructor() {
        super();
        this._fields = [];
        const style = document.createElement('style');
        style.textContent = `
          :host { display: grid; gap: var(--system-form-gap, .7rem); color: var(--system-form-color, inherit); font: inherit; }
          form, [part='fields'] { display: grid; gap: .7rem; }
          [part='field'] { display: grid; gap: .25rem; }
          label, legend { font-weight: 600; }
          input:not([type='checkbox']), select, textarea { box-sizing: border-box; width: 100%; border: 1px solid var(--system-form-border-color, currentColor); border-radius: var(--system-form-radius, .45rem); background: var(--system-form-input-background, transparent); color: inherit; font: inherit; padding: .5rem; }
          textarea { min-height: 6rem; resize: vertical; }
          button { justify-self: start; cursor: pointer; border: 1px solid var(--system-form-border-color, currentColor); border-radius: var(--system-form-radius, .45rem); background: var(--system-form-button-background, transparent); color: inherit; font: inherit; padding: .55rem .8rem; }
          button:disabled { cursor: not-allowed; opacity: .65; }
          [part='description'], [part='status'], [part='help'], [part='summary'], [part='contract'], [part='affected-label'], [part='warning'], [part='fingerprint'], [part='error'] { margin: 0; overflow-wrap: anywhere; }
          [part='status'], [part='help'], [part='contract'] { font-size: .85rem; }
          [part='proposal'], [part='receipt'] { display: grid; gap: .5rem; border: 1px solid var(--system-form-border-color, currentColor); border-radius: var(--system-form-radius, .45rem); padding: .75rem; }
          [part='proposal'] h3, [part='receipt'] h3 { margin: 0; font-size: 1rem; }
          pre { max-height: 15rem; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; }
          output { overflow-wrap: anywhere; }
        `;
        this._description = document.createElement('p');
        this._description.setAttribute('part', 'description');
        this._form = document.createElement('form');
        this._form.noValidate = false;
        this._fieldsHost = document.createElement('div');
        this._fieldsHost.setAttribute('part', 'fields');
        this._submit = document.createElement('button');
        this._submit.type = 'submit';
        this._submit.textContent = 'Review system request';
        this._submit.setAttribute('part', 'submit');
        this._submit.disabled = true;
        this._form.append(this._fieldsHost, this._submit);
        this._form.addEventListener('submit', event => {
          event.preventDefault();
          if (!this._form.reportValidity()) return;
          try { this._prepare(this._collect()); }
          catch (error) {
            this._setStatus(error.message, true);
            this._emit('system-error', {code: error.code || 'SYSTEM_FORM_INPUT_INVALID'});
          }
        });
        this._status = document.createElement('p');
        this._status.setAttribute('part', 'status');
        this._status.setAttribute('aria-live', 'polite');
        this._result = document.createElement('section');
        this._result.setAttribute('part', 'result-area');
        this._result.setAttribute('aria-label', 'System form result');
        this.shadowRoot.append(style, this._description, this._form, this._status, this._result);
      }

      connectedCallback() { this._connect(); }
      disconnectedCallback() { this._disconnect(); }
      attributeChangedCallback(name, oldValue, newValue) {
        if (name === 'capability-id' && oldValue !== newValue) this._capabilityChanged();
      }

      _renderDescriptor(descriptor) {
        this._description.textContent = descriptor.description;
        this._renderFields(descriptor.inputSchema);
      }

      _renderFields(schema) {
        if (schema.type !== 'object' || schema.additionalProperties !== false ||
            !schema.properties || Array.isArray(schema.properties) || typeof schema.properties !== 'object') {
          throw new SystemComponentError(
            'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability does not have a renderable closed object form.');
        }
        const properties = Object.entries(schema.properties);
        if (properties.length > 64) throw new SystemComponentError(
          'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability has too many form fields.');
        const required = new Set(Array.isArray(schema.required) ? schema.required : []);
        if ([...required].some(name => !Object.hasOwn(schema.properties, name))) throw new SystemComponentError(
          'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability has an invalid required-field contract.');
        const fragment = document.createDocumentFragment();
        const fields = [];
        for (const [name, fieldSchema] of properties) {
          if (!name || name.length > 200 || !fieldSchema || Array.isArray(fieldSchema) ||
              typeof fieldSchema !== 'object') throw new SystemComponentError(
            'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability has an invalid field contract.');
          const field = this._field(name, fieldSchema, required.has(name));
          fields.push(field);
          fragment.append(field.host);
        }
        this._fields = fields;
        this._fieldsHost.replaceChildren(fragment);
      }

      _field(name, schema, required) {
        const host = document.createElement('div');
        host.setAttribute('part', 'field');
        const id = 'system-field-' + systemComponentRandomId();
        const label = document.createElement('label');
        label.htmlFor = id;
        label.textContent = name + (required ? ' (required)' : '');
        let control;
        let kind;
        if (Object.hasOwn(schema, 'const')) {
          control = document.createElement('output');
          control.id = id;
          control.textContent = JSON.stringify(schema.const);
          kind = 'const';
        } else if (Array.isArray(schema.enum)) {
          if (schema.enum.length < 1 || schema.enum.length > 200 ||
              schema.enum.some(value => typeof value !== 'string')) throw new SystemComponentError(
            'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability has an unsupported choice field.');
          control = document.createElement('select');
          control.id = id;
          control.required = required;
          for (const value of schema.enum) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = value;
            control.append(option);
          }
          kind = 'string';
        } else if (schema.type === 'string') {
          const maximum = Number.isInteger(schema.maxLength) ? schema.maxLength : 8000;
          if (maximum < 0 || maximum > 65536) throw new SystemComponentError(
            'SYSTEM_FORM_SCHEMA_UNSUPPORTED', 'This capability has an unsupported text bound.');
          control = maximum > 500 || name.toLowerCase().includes('json')
            ? document.createElement('textarea') : document.createElement('input');
          if (control instanceof HTMLInputElement) control.type = 'text';
          control.id = id;
          control.required = required && (schema.minLength || 0) > 0;
          control.maxLength = maximum;
          if (Number.isInteger(schema.minLength) && schema.minLength > 0) control.minLength = schema.minLength;
          kind = 'string';
        } else if (schema.type === 'integer' || schema.type === 'number') {
          control = document.createElement('input');
          control.type = 'number';
          control.id = id;
          control.required = required;
          control.step = schema.type === 'integer' ? '1' : 'any';
          if (typeof schema.minimum === 'number') control.min = String(schema.minimum);
          if (typeof schema.maximum === 'number') control.max = String(schema.maximum);
          kind = schema.type;
        } else if (schema.type === 'boolean') {
          control = document.createElement('input');
          control.type = 'checkbox';
          control.id = id;
          kind = 'boolean';
        } else if (schema.type === 'array' || schema.type === 'object') {
          control = document.createElement('textarea');
          control.id = id;
          control.required = required;
          control.maxLength = 65536;
          control.value = schema.type === 'array' ? '[]' : '{}';
          control.setAttribute('spellcheck', 'false');
          kind = schema.type;
        } else {
          throw new SystemComponentError(
            'SYSTEM_FORM_SCHEMA_UNSUPPORTED', `The field ${name} cannot be rendered safely.`);
        }
        control.setAttribute('part', 'input');
        host.append(label, control);
        const help = document.createElement('p');
        help.setAttribute('part', 'help');
        help.textContent = this._help(schema, kind);
        if (help.textContent) {
          help.id = id + '-help';
          control.setAttribute('aria-describedby', help.id);
          host.append(help);
        }
        return {name, schema, required, control, kind, host};
      }

      _help(schema, kind) {
        if (kind === 'const') return 'This value is fixed by the system contract.';
        if (kind === 'array' || kind === 'object') return `Enter one ${kind} as JSON.`;
        const bounds = [];
        if (schema.minLength != null) bounds.push(`minimum length ${schema.minLength}`);
        if (schema.maxLength != null) bounds.push(`maximum length ${schema.maxLength}`);
        if (schema.minimum != null) bounds.push(`minimum ${schema.minimum}`);
        if (schema.maximum != null) bounds.push(`maximum ${schema.maximum}`);
        return bounds.join(', ');
      }

      _collect() {
        const result = Object.create(null);
        for (const field of this._fields) {
          const {name, schema, required, control, kind} = field;
          if (kind === 'const') {
            result[name] = schema.const;
          } else if (kind === 'boolean') {
            result[name] = control.checked;
          } else if (kind === 'integer' || kind === 'number') {
            if (control.value === '' && !required) continue;
            const value = control.valueAsNumber;
            if (!Number.isFinite(value) || (kind === 'integer' && !Number.isInteger(value))) {
              throw new SystemComponentError('SYSTEM_FORM_INPUT_INVALID', `${name} must be a valid ${kind}.`);
            }
            result[name] = value;
          } else if (kind === 'array' || kind === 'object') {
            if (control.value.trim() === '' && !required) continue;
            let value;
            try { value = JSON.parse(control.value); }
            catch { throw new SystemComponentError(
              'SYSTEM_FORM_INPUT_INVALID', `${name} must contain valid JSON.`); }
            if (kind === 'array' ? !Array.isArray(value) : value === null || Array.isArray(value) || typeof value !== 'object') {
              throw new SystemComponentError('SYSTEM_FORM_INPUT_INVALID', `${name} must contain one JSON ${kind}.`);
            }
            result[name] = value;
          } else {
            if (control.value === '' && !required) continue;
            result[name] = control.value;
          }
        }
        return systemComponentClone(result);
      }

      _setReady(value) {
        this._submit.disabled = !value;
        for (const field of this._fields) {
          if ('disabled' in field.control) field.control.disabled = !value;
        }
      }
    }

    if (!customElements.get('system-navigation')) {
      customElements.define('system-navigation', SystemNavigation);
    }
    if (!customElements.get('system-chat')) {
      customElements.define('system-chat', SystemChat);
    }
    if (!customElements.get('system-action-button')) {
      customElements.define('system-action-button', SystemActionButton);
    }
    if (!customElements.get('system-form')) {
      customElements.define('system-form', SystemForm);
    }
    """;
}
