import { createServer } from 'node:http';
import assert from 'node:assert/strict';

export const playerText = 'I offer to help. May I join your watch?';
export const guideText = 'The guide says, “You are welcome here, Mira.”';

// A scripted model boundary makes this a deterministic orchestration acceptance test, not a
// model-quality benchmark. No gateway, verifier, executor, persistence, or query is substituted.
export async function scriptedModel(evidence) {
  const server = createServer(async (request, response) => {
    try {
      let raw = '';
      for await (const chunk of request) {
        raw += chunk;
        assert.ok(raw.length < 200000, 'Scripted model request exceeded its bound');
      }
      const body = raw ? JSON.parse(raw) : null;
      let result;
      if (request.url === '/api/tags') result = { models: [{ name: 'slice19-scripted', model: 'slice19-scripted', digest: 'slice19-scripted-not-a-real-model' }] };
      else if (request.url === '/api/show') result = { capabilities: ['completion'] };
      else {
        assert.equal(request.url, '/api/chat');
        assert.equal(body.model, 'slice19-scripted');
        assert.ok(!body.tools?.length, 'The conversation fixture cannot issue tools');
        const turn = JSON.parse(body.messages.find(value => value.role === 'user').content);
        assert.equal(turn.PlayerText, playerText);
        const decision = { decision: 'respond', text: guideText,
          situation: { transition: 'replace', kind: 'conversation', summary: 'The guide welcomes Mira.',
            participants: [{ name: 'Mira', entityId: null }, { name: 'The guide', entityId: null }], location: null },
          truths: [{ statement: 'The guide welcomed Mira.', subjectEntityIds: [] }] };
        result = { model: body.model, done: true, message: { role: 'assistant', content: JSON.stringify(decision) }, prompt_eval_count: 100, eval_count: 80 };
      }
      await evidence('scripted-model-boundary', { path: request.url, request: body, response: result, driver: 'scripted fixture; no real model inference' });
      response.writeHead(200, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify(result));
    } catch (error) {
      response.writeHead(500, { 'Content-Type': 'application/json' });
      response.end(JSON.stringify({ error: error.message }));
    }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  return { env: { InteractionOuter__Local__Enabled: 'true', InteractionOuter__Local__Endpoint: `http://127.0.0.1:${server.address().port}/`,
    InteractionOuter__Local__Model: 'slice19-scripted', InteractionOuter__Provider: 'Local' },
  close: () => new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())) };
}
