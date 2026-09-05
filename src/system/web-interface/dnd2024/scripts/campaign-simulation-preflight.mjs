import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readFile, cp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../..');
const applicationId = 'dnd2024';
const worldId = 'slice19-world';
const hash = value => createHash('sha256').update(value).digest('hex').toUpperCase();
const stages = ['bootstrap', 'exploration', 'travel-hazard', 'conversation-social',
  'combat-movement', 'loot-inventory', 'rest', 'downtime', 'restart-resume', 'web-parity', 'audience-isolation'];

// Never accepts an existing listener or database: every run owns its process and storage.
export function requireIsolatedOrigin(origin) {
  const url = new URL(origin);
  assert.equal(url.hostname, '127.0.0.1');
  assert.equal(url.protocol, 'http:');
  assert.ok(url.port && !['6217', '5144'].includes(url.port));
  assert.equal(url.pathname, '/');
  assert.ok(!url.username && !url.password && !url.search && !url.hash);
  return url.origin;
}

export function parseRpc(text) {
  const payload = text.trimStart().startsWith('{') ? text
    : text.split(/\r?\n/).filter(line => line.startsWith('data:')).map(line => line.slice(5).trim()).join('\n');
  const message = JSON.parse(payload);
  if (message.error) throw new Error(JSON.stringify(message.error));
  assert.ok(message.result, 'Missing JSON-RPC result');
  return message.result;
}

export function acceptanceStatus(stageResults) {
  if (stages.some(stage => stageResults[stage] === 'failed')) return 'blocked';
  return stages.every(stage => stageResults[stage] === 'passed') ? 'passed' : 'incomplete';
}

export function preflightExitCode(stageResults) {
  return stageResults.bootstrap === 'passed' ? 0 : 1;
}

async function command(file, args, cwd, env = process.env, onText = () => {}) {
  const child = spawn(file, args, { cwd, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  child.stdout.on('data', chunk => { output += chunk; onText(String(chunk)); });
  child.stderr.on('data', chunk => { output += chunk; onText(String(chunk)); });
  const done = new Promise(accept => {
    child.once('error', error => accept({ code: -1, output: output + error.message }));
    child.once('exit', code => accept({ code, output }));
  });
  return { child, done };
}

export async function run({ scenario, prepareCatalog, modelDriver } = {}) {
  const root = await mkdtemp(join(tmpdir(), 'dnd-slice19-simulation-'));
  console.log(`Evidence: ${root}`);
  const report = { schemaVersion: 1, evidenceDirectory: root, scope: scenario ? 'slice19-public-campaign-flow' : 'slice19-public-bootstrap-preflight', status: 'running', startedAt: new Date().toISOString(),
    stages: Object.fromEntries(stages.map(stage => [stage, 'not-run'])), evidence: [] };
  let server;
  let model;
  let evidenceIndex = 0;
  let requestId = 0;
  let origin;
  let lastWebWriteAt = 0;
  let stage = 'bootstrap';
  async function evidence(label, value) {
    const name = `${String(evidenceIndex++).padStart(4, '0')}-${label.replaceAll(/[^a-z0-9.-]/gi, '-')}.json`;
    const bytes = JSON.stringify(value, null, 2) + '\n';
    await writeFile(join(root, name), bytes);
    assert.equal(hash(await readFile(join(root, name))), hash(bytes));
    report.evidence.push({ name, sha256: hash(bytes) });
  }
  async function tool(name, args) {
    const response = await fetch(requireIsolatedOrigin(origin) + '/mcp', {
      method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json, text/event-stream' },
      body: JSON.stringify({ jsonrpc: '2.0', id: ++requestId, method: 'tools/call', params: { name, arguments: args } }),
      signal: AbortSignal.timeout(120000),
    });
    const raw = await response.text();
    await evidence(`${stage}-${name}-${args.kind ?? ''}`, { request: args, status: response.status, response: raw });
    assert.ok(response.ok, `HTTP ${response.status}: ${raw.slice(0, 1000)}`);
    const result = parseRpc(raw);
    assert.ok(!result.isError, JSON.stringify(result));
    const envelope = JSON.parse(result.content.find(value => value.type === 'text').text);
    assert.ok(envelope.ok === true && !envelope.error, JSON.stringify(envelope));
    return envelope.data;
  }
  async function http(path, body, expectedStatus = 200) {
    assert.ok(path.startsWith('/api/'), 'Use a public web contract');
    // Exercise the production ten-writes-per-minute policy instead of disabling it for tests.
    // Every confirmation and replay retains its original request and idempotency key.
    if (body !== undefined) {
      const delay = Math.max(0, lastWebWriteAt + 6500 - Date.now());
      if (delay) await new Promise(resolve => setTimeout(resolve, delay));
      lastWebWriteAt = Date.now();
    }
    const response = await fetch(requireIsolatedOrigin(origin) + path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { 'Content-Type': 'application/json', Origin: origin },
      body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(120000),
    });
    const raw = await response.text();
    await evidence(`${stage}-web`, { path, body, status: response.status, response: raw });
    assert.equal(response.status, expectedStatus, `HTTP ${response.status}: ${raw.slice(0, 2000)}`);
    return JSON.parse(raw);
  }
  try {
    for (const [project, directory] of [['DantesRoleplay.Tools', 'tools'], ['DantesRoleplay.MCPServer', 'server']]) {
      const build = await command('dotnet', ['build', join(repo, project, `${project}.csproj`), '--no-restore', '--output', join(root, directory)], repo);
      const result = await build.done;
      await evidence(`build-${directory}`, result);
      assert.equal(result.code, 0, result.output);
    }
    const catalog = join(root, 'catalog');
    await mkdir(join(catalog, 'world/entities'), { recursive: true });
    await mkdir(join(catalog, 'namespaces'), { recursive: true });
    await cp(join(repo, 'catalog/namespaces'), join(catalog, 'namespaces'), { recursive: true });
    await writeFile(join(catalog, 'namespaces/_root.json'), JSON.stringify({ id: 'catalog-root', owner: 'slice19-fixture', description: 'Disposable synthetic fixture identity only.', allowedKinds: ['entity'], aliases: [], enabled: true, reviewStatus: 'reviewed', reviewNote: 'Synthetic test fixture; never imported into live storage.' }));
    await writeFile(join(catalog, 'manifest.json'), JSON.stringify({ schemaVersion: 2, exportedAt: '2026-09-05T00:00:00Z', sourceDatabase: 'synthetic-fixture-only', includesWorld: true, records: [] }));
    await writeFile(join(catalog, 'world/entities/slice19-world.json'), JSON.stringify({ id: worldId, name: 'Slice 19 disposable world', components: {} }));
    if (prepareCatalog) await prepareCatalog({ catalog, repo });
    const setup = await command('dotnet', [join(root, 'tools/roleplay.dll'), 'setup', catalog, '--database', join(root, 'runtime.db')], root);
    const setupResult = await setup.done;
    await evidence('catalog-setup', setupResult);
    assert.equal(setupResult.code, 0, setupResult.output);
    const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production',
      ConnectionStrings__Kernel: join(root, 'runtime.db'), BlobStorage__Root: join(root, 'blobs'),
      Sources__AllowedRoots__repository: repo, Catalogs__PublishedApplications__0: applicationId,
      Knowledge__LocalPlayer__Enabled: 'true', Knowledge__LocalPlayer__Role: 'GameMaster',
      Knowledge__LocalPlayer__PrincipalId: 'slice19.operator', Knowledge__LocalPlayer__ApplicationId: applicationId,
      Knowledge__LocalPlayer__CampaignId: 'slice19-campaign', Knowledge__LocalPlayer__ActorId: 'slice19-actor',
      Logging__LogLevel__Default: 'Warning', Logging__LogLevel__Microsoft_Hosting_Lifetime: 'Information',
      Retrieval__Embedding__Enabled: 'false', Knowledge__Completion__Enabled: 'false',
      Retrieval__DerivedDataDirectory: join(root, 'derived'),
      InteractionOuter__Local__Enabled: 'false', InteractionPlanning__Remote__Enabled: 'false' };
    delete env.OPENAI_API_KEY;
    if (modelDriver) { model = await modelDriver(evidence); Object.assign(env, model.env); }
    // Bootstrap restarts alone do not imply campaign resume. The scenario must compare
    // persisted gameplay, conversation, and read-model state after an additional true restart.
    async function start(phase) {
      if (server) {
        server.child.kill();
        await evidence('server-process-before-restart', await server.done);
        server = null;
      }
      let readyResolve;
      const ready = new Promise(accept => { readyResolve = accept; });
      let startup = '';
      server = await command(join(root, 'server/DantesRoleplay.MCPServer.exe'), ['--urls', 'http://127.0.0.1:0', '--Logging:LogLevel:Microsoft.Hosting.Lifetime', 'Information'], root, env, text => {
        startup += text;
        const match = startup.match(/Now listening on:\s+(http:\/\/127\.0\.0\.1:\d+)/);
        if (match) readyResolve(match[1]);
      });
      let timer;
      try {
        origin = requireIsolatedOrigin(await Promise.race([ready, server.done.then(result => { throw new Error(result.output); }),
          new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Isolated server startup timed out')), 60000); })]));
      } finally { clearTimeout(timer); }
      console.log(`${phase} listener: ${origin}`);
      await tool('query', { kind: 'capabilities' });
    }
    await start('cold-start');
    await start('bootstrap-restart');
    report.stages.bootstrap = 'passed';
    if (scenario) await scenario({ tool, http, evidence, repo, root, report, restart: (overrides = {}) => { Object.assign(env, overrides); return start('campaign-resume'); },
      begin(name) { assert.ok(stages.includes(name)); stage = name; console.log(`Stage: ${name}`); },
      pass(name) { assert.ok(stages.includes(name)); report.stages[name] = 'passed'; } });
  } catch (error) {
    report.status = 'blocked';
    report.stages[stage] = 'failed';
    report.failure = error.message;
    console.error(error.message.slice(0, 3000));
    process.exitCode = 1;
  } finally {
    if (server) {
      server.child.kill();
      await evidence('server-process', await server.done);
    }
    if (model) await model.close();
    report.status = acceptanceStatus(report.stages);
    report.preflightStatus = report.stages.bootstrap;
    process.exitCode = scenario ? (report.status === 'passed' ? 0 : 1) : preflightExitCode(report.stages);
    report.finishedAt = new Date().toISOString();
    for (const item of report.evidence) assert.equal(hash(await readFile(join(root, item.name))), item.sha256);
    await writeFile(join(root, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(`Preflight: ${report.preflightStatus}; campaign flow: ${report.status}; retained evidence and disposable database: ${root}`);
  }
  return report;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await run();
