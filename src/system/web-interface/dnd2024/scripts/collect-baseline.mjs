import { createHash } from "node:crypto";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { request as httpRequest } from "node:http";
import { request as httpsRequest } from "node:https";
import { cpus, platform, release, totalmem } from "node:os";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const webRoot = resolve(dirname(scriptPath), "..");
const repositoryRoot = resolve(webRoot, "../../../..");
const node = process.execPath;
const git = process.env.GIT_EXECUTABLE || "git";
const defaultListeners = ["http://127.0.0.1:6217", "https://127.0.0.1:5144"];

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function run(executable, args, cwd, command) {
  const result = spawnSync(executable, args, {
    cwd,
    encoding: "utf8",
    env: process.env,
    maxBuffer: 16 * 1024 * 1024,
  });
  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`.trim();
  return {
    command,
    exitCode: result.status ?? 1,
    status: result.status === 0 ? "passed" : "failed",
    rawOutput: output,
  };
}

function gateEvidence(result, kind) {
  const lines = result.rawOutput.split(/\r?\n/u);
  if (kind === "tests") {
    const summary = Object.fromEntries(lines.flatMap((line) => {
      const match = line.match(/^ℹ (tests|pass|fail|cancelled|skipped|todo) (\d+)$/u);
      return match ? [[match[1], Number(match[2])]] : [];
    }));
    return { command: result.command, exitCode: result.exitCode, status: result.status, summary };
  }
  if (kind === "typecheck") {
    const diagnostics = lines.filter((line) => /error TS\d+:/u.test(line));
    return {
      command: result.command,
      exitCode: result.exitCode,
      status: result.status,
      diagnosticCount: diagnostics.length,
      diagnostics,
    };
  }
  return { command: result.command, exitCode: result.exitCode, status: result.status };
}

function gitOutput(args) {
  return execFileSync(git, args, {
    cwd: repositoryRoot,
    encoding: "utf8",
    maxBuffer: 32 * 1024 * 1024,
    stdio: ["ignore", "pipe", "pipe"],
  }).trim();
}

function worktreeEvidence(excludedPath) {
  const commit = gitOutput(["rev-parse", "HEAD"]);
  const trackedDiff = execFileSync(git, ["-c", "core.autocrlf=false", "diff", "--binary", "--no-ext-diff", "HEAD"], {
    cwd: repositoryRoot,
    maxBuffer: 64 * 1024 * 1024,
    stdio: ["ignore", "pipe", "pipe"],
  });
  const excludedRelative = excludedPath ? relative(repositoryRoot, excludedPath).replaceAll("\\", "/") : null;
  const untracked = gitOutput(["ls-files", "--others", "--exclude-standard"])
    .split(/\r?\n/u)
    .filter((path) => path && path !== excludedRelative)
    .sort();
  const untrackedManifest = untracked.map((path) => {
    const bytes = readFileSync(resolve(repositoryRoot, path));
    return `${sha256(bytes)}  ${path}`;
  }).join("\n");
  const trackedDiffSha256 = sha256(trackedDiff);
  const untrackedManifestSha256 = sha256(untrackedManifest);
  return {
    commit,
    branch: gitOutput(["branch", "--show-current"]),
    trackedDiffSha256,
    untrackedManifestSha256,
    untrackedFileCount: untracked.length,
    fingerprint: sha256(JSON.stringify({ commit, trackedDiffSha256, untrackedManifestSha256 })),
  };
}

function filesBelow(root) {
  if (!statSync(root).isDirectory()) return [root];
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(root, entry.name);
    return entry.isDirectory() ? filesBelow(path) : [path];
  });
}

function bundleEvidence() {
  const bundleRoot = resolve(webRoot, "server-dist");
  const files = filesBelow(bundleRoot).sort();
  const hash = createHash("sha256");
  const artifacts = files.map((path) => {
    const bytes = readFileSync(path);
    const name = relative(bundleRoot, path).replaceAll("\\", "/");
    hash.update(name).update("\0").update(bytes).update("\0");
    return { path: name, bytes: bytes.byteLength, sha256: sha256(bytes) };
  });
  return {
    bytes: artifacts.reduce((total, artifact) => total + artifact.bytes, 0),
    sha256: hash.digest("hex"),
    artifacts,
  };
}

function requestBuffer(url, timeoutMs = 5000) {
  return new Promise((resolveRequest, rejectRequest) => {
    const parsed = new URL(url);
    const request = (parsed.protocol === "https:" ? httpsRequest : httpRequest)(parsed, {
      headers: { Accept: "application/json, text/html;q=0.9, */*;q=0.1" },
      rejectUnauthorized: false,
    }, (response) => {
      const chunks = [];
      response.on("data", (chunk) => chunks.push(chunk));
      response.on("end", () => resolveRequest({
        status: response.statusCode ?? 0,
        headers: response.headers,
        body: Buffer.concat(chunks),
      }));
    });
    request.setTimeout(timeoutMs, () => request.destroy(new Error("timeout")));
    request.on("error", rejectRequest);
    request.end();
  });
}

async function livePageEvidence(origin) {
  const pagePath = "/ui/dnd2024-play";
  const pageResponse = await requestBuffer(new URL(pagePath, origin));
  if (pageResponse.status < 200 || pageResponse.status >= 400) {
    throw new Error(`page returned HTTP ${pageResponse.status}`);
  }

  const html = pageResponse.body.toString("utf8");
  const assets = [...html.matchAll(/(?:src|href)=["']([^"']+)["']/gu)]
    .map((match) => new URL(match[1], origin))
    .filter((url) => url.origin === new URL(origin).origin && url.pathname.startsWith("/ui/dnd2024-play/"));
  const uniqueAssets = [...new Map(assets.map((url) => [url.href, url])).values()];
  const bundleItems = [{ path: pagePath, body: pageResponse.body }];
  for (const asset of uniqueAssets) {
    const response = await requestBuffer(asset);
    if (response.status >= 200 && response.status < 400) bundleItems.push({ path: asset.pathname, body: response.body });
  }
  bundleItems.sort((left, right) => left.path.localeCompare(right.path));
  const bundleHash = createHash("sha256");
  for (const item of bundleItems) bundleHash.update(item.path).update("\0").update(item.body).update("\0");

  let pageDiagnostic = null;
  try {
    const response = await requestBuffer(new URL(
      "/api/control/web/applications/dnd2024/pages/dnd2024-play",
      origin,
    ));
    if (response.status >= 200 && response.status < 300) pageDiagnostic = JSON.parse(response.body.toString("utf8"));
  } catch {
    pageDiagnostic = null;
  }
  const entityId = pageDiagnostic?.page?.entityId ?? null;
  let activeRevision = null;
  if (entityId) {
    try {
      const response = await requestBuffer(new URL(
        `/api/control/web/applications/dnd2024/pages/${encodeURIComponent(entityId)}`,
        origin,
      ));
      if (response.status >= 200 && response.status < 300) {
        const page = JSON.parse(response.body.toString("utf8"));
        activeRevision = page.activeRevision ?? page.summary?.activeRevision ?? null;
      }
    } catch {
      activeRevision = null;
    }
  }

  return {
    status: "available",
    listener: origin,
    pageStatus: pageResponse.status,
    activeRevision,
    activeEntityId: entityId,
    bundleSha256: bundleHash.digest("hex"),
    bundleBytes: bundleItems.reduce((total, item) => total + item.body.byteLength, 0),
    assetCount: bundleItems.length - 1,
  };
}

export function summarizeSamples(values) {
  if (!Array.isArray(values) || values.length === 0 || values.some((value) => !Number.isFinite(value))) return null;
  const sorted = [...values].sort((left, right) => left - right);
  const percentile = (value) => sorted[Math.min(sorted.length - 1, Math.ceil(value * sorted.length) - 1)];
  return {
    sampleCount: sorted.length,
    p50Ms: percentile(0.5),
    p95Ms: percentile(0.95),
  };
}

function browserEvidence(resultsPath, expectedListener, browser) {
  if (!resultsPath) {
    return {
      status: "blocked",
      reason: "No live listener was available, so browser cold/warm sampling was not run.",
      requiredSamples: { cold: 20, warm: 20 },
      browser,
      listener: expectedListener,
    };
  }
  const source = JSON.parse(readFileSync(resultsPath, "utf8"));
  const groups = { cold: [], warm: [] };
  for (const run of source.runs ?? []) {
    if (run.cacheState === "cold" || run.cacheState === "warm") groups[run.cacheState].push(run);
  }
  const metrics = {};
  for (const [cacheState, runs] of Object.entries(groups)) {
    metrics[cacheState] = {};
    const names = [...new Set(runs.flatMap((run) => Object.keys(run.marks ?? {})))].sort();
    for (const name of names) {
      const summary = summarizeSamples(runs.map((run) => run.marks?.[name]).filter(Number.isFinite));
      if (summary) metrics[cacheState][name] = summary;
    }
  }
  return {
    status: groups.cold.length >= 20 && groups.warm.length >= 20 ? "complete" : "insufficient-samples",
    requiredSamples: { cold: 20, warm: 20 },
    observedSamples: { cold: groups.cold.length, warm: groups.warm.length },
    browser: source.browser ?? browser,
    listener: source.listener ?? expectedListener,
    metrics,
  };
}

function argumentsFrom(argv) {
  const result = { listeners: [], output: null, browserResults: null, browserName: "unspecified", browserVersion: "unspecified" };
  for (let index = 0; index < argv.length; index += 1) {
    const value = argv[index + 1];
    if (argv[index] === "--listener" && value) { result.listeners.push(value); index += 1; }
    else if (argv[index] === "--output" && value) { result.output = resolve(value); index += 1; }
    else if (argv[index] === "--browser-results" && value) { result.browserResults = resolve(value); index += 1; }
    else if (argv[index] === "--browser-name" && value) { result.browserName = value; index += 1; }
    else if (argv[index] === "--browser-version" && value) { result.browserVersion = value; index += 1; }
  }
  if (result.listeners.length === 0) result.listeners = defaultListeners;
  return result;
}

async function main() {
  const options = argumentsFrom(process.argv.slice(2));
  const testRun = run(node, ["--test"], webRoot, "node --test");
  const typecheckRun = run(node, [resolve(webRoot, "node_modules/typescript/bin/tsc"), "--noEmit"], webRoot,
    "node node_modules/typescript/bin/tsc --noEmit");
  const serverBuildRun = run(node,
    [resolve(webRoot, "node_modules/vite/bin/vite.js"), "build", "--config", "vite.server.config.ts"],
    webRoot, "node node_modules/vite/bin/vite.js build --config vite.server.config.ts");
  const gates = {
    tests: gateEvidence(testRun, "tests"),
    typecheck: gateEvidence(typecheckRun, "typecheck"),
    serverBuild: gateEvidence(serverBuildRun, "serverBuild"),
  };

  const probes = [];
  let live = null;
  for (const listener of options.listeners) {
    try {
      live = await livePageEvidence(listener);
      probes.push({ listener, status: "available" });
      break;
    } catch (error) {
      probes.push({ listener, status: "unavailable", reason: error instanceof Error ? error.message : String(error) });
    }
  }
  if (!live) live = {
    status: "blocked",
    reason: "Neither configured listener served /ui/dnd2024-play; active revision, live bundle, and browser timings were not inferred from build output.",
    probes,
    activeRevision: null,
    bundleSha256: null,
  };

  const browser = { name: options.browserName, version: options.browserVersion };
  const report = {
    schema: "dnd2024.website-baseline.v1",
    generatedAtUtc: new Date().toISOString(),
    source: worktreeEvidence(options.output),
    target: {
      machine: {
        profile: "current-local-development-machine",
        platform: platform(),
        release: release(),
        cpu: cpus()[0]?.model ?? "unknown",
        logicalProcessors: cpus().length,
        memoryGiB: Number((totalmem() / 1024 ** 3).toFixed(1)),
      },
      browser,
    },
    gates,
    sourceBundle: gates.serverBuild.status === "passed" ? bundleEvidence() : null,
    live,
    browserMeasurements: browserEvidence(options.browserResults, live.listener ?? null, browser),
    invariants: {
      livePagePublishedOrActivated: false,
      privatePayloadBodiesRecorded: false,
    },
  };

  const json = `${JSON.stringify(report, null, 2)}\n`;
  if (options.output) {
    mkdirSync(dirname(options.output), { recursive: true });
    writeFileSync(options.output, json);
  } else {
    process.stdout.write(json);
  }
  if (gates.tests.status !== "passed" || gates.serverBuild.status !== "passed") process.exitCode = 1;
}

if (resolve(process.argv[1] ?? "") === scriptPath) await main();
