namespace DantesRoleplay.Web.Interactions;

public static class ApplicationConversationElement
{
    public const string Script = """
    class ApplicationConversation extends HTMLElement {
      connectedCallback() {
        this.replaceChildren();
        const log = document.createElement('div');
        log.setAttribute('role', 'log');
        const input = document.createElement('textarea');
        input.placeholder = 'What do you want to do?';
        const send = document.createElement('button');
        send.textContent = 'Send';
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
        this.append(log, input, send, rememberLabel, confirm, status);
        const applicationId = this.getAttribute('application-id');
        const stateSpaceId = this.getAttribute('state-space-id');
        const sessionContextId = this.getAttribute('session-context-id');
        let conversationId = null;
        const emit = (name, detail) => this.dispatchEvent(new CustomEvent(name, {detail}));
        const show = value => {
          log.replaceChildren();
          for (const message of value.messages || []) {
            const line = document.createElement('p');
            line.textContent = `${message.role}: ${message.text}`;
            log.append(line);
          }
          status.textContent = value.status || '';
          confirm.hidden = value.status !== 'awaiting-confirmation';
          rememberLabel.hidden = value.status !== 'awaiting-confirmation';
          if (value.pendingPlan) emit('proposal', value.pendingPlan);
        };
        const ensure = async () => {
          if (conversationId) return;
          if (!applicationId || !stateSpaceId || !sessionContextId) throw new Error('Application, state space, and session context are required.');
          emit('progress', {phase:'create'});
          const response = await fetch(`/api/applications/${encodeURIComponent(applicationId)}/conversations`, {
            method: 'POST', headers: {'content-type':'application/json'},
            body: JSON.stringify({stateSpaceId, sessionContextId})
          });
          if (!response.ok) throw new Error('Could not start the conversation.');
          const value = await response.json();
          conversationId = value.id;
          show(value);
        };
        send.addEventListener('click', async () => {
          try {
            await ensure();
            emit('progress', {phase:'turn'});
            const response = await fetch(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/turns`, {
              method: 'POST', headers: {'content-type':'application/json'},
              body: JSON.stringify({text: input.value})
            });
            const value = await response.json();
            if (!response.ok) throw new Error(value.message || 'The turn failed.');
            input.value = '';
            show(value);
          } catch (error) { status.textContent = error.message; emit('error', {message:error.message}); }
        });
        confirm.addEventListener('click', async () => {
          try {
            emit('progress', {phase:'execute'});
            const response = await fetch(`/api/applications/${encodeURIComponent(applicationId)}/conversations/${encodeURIComponent(conversationId)}/execute`, {
              method: 'POST', headers: {'content-type':'application/json'}, body: JSON.stringify({learn: remember.checked})
            });
            const value = await response.json();
            if (!response.ok) throw new Error(value.message || 'Execution failed.');
            show(value);
            remember.checked = false;
            emit('receipt', value.lastExecution && value.lastExecution.receipt);
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
