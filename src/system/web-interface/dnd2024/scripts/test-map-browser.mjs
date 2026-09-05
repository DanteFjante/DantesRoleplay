import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';
import react from '@vitejs/plugin-react';

const root = fileURLToPath(new URL('..', import.meta.url));
const options = { output: resolve(root, '.tmp/website-slice-2/browser.json') };
for (let i = 2; i < process.argv.length; i++) {
  const name = process.argv[i]; const value = process.argv[++i];
  assert.ok(value, 'Missing argument for ' + name);
  if (name === '--playwright-module') options.module = pathToFileURL(resolve(value)).href;
  else if (name === '--browser-executable') options.executable = resolve(value);
  else if (name === '--output') options.output = resolve(value);
  else throw new Error('Unknown option ' + name);
}

const sourceFiles = ['src/components/MapCanvas.tsx', 'src/components/TacticalBoard.tsx', 'src/styles.css',
  'test/visual/map-interaction-fixture.html', 'test/visual/map-interaction-fixture.tsx', 'scripts/test-map-browser.mjs'];
const report = { generatedAtUtc: new Date().toISOString(),
  target: 'isolated maintained-source fixture, not the published game page', cases: [],
  sources: Object.fromEntries(await Promise.all(sourceFiles.map(async path =>
    [path, createHash('sha256').update(await readFile(resolve(root, path))).digest('hex')]))),
};
const server = await createServer({ configFile: false, root, plugins: [react()],
  server: { host: '127.0.0.1', port: 0, strictPort: true }, logLevel: 'error' });
let browser;
try {
  await server.listen();
  const { chromium } = await import(options.module ?? 'playwright');
  browser = await chromium.launch({ headless: true, ...(options.executable ? { executablePath: options.executable } : {}) });
  report.browser = { name: 'Chromium', version: browser.version() };
  report.listener = server.resolvedUrls.local[0];
  for (const profile of [
    { mobile: false, width: 1280, height: 900 },
    { mobile: true, width: 390, height: 844 },
    { mobile: true, width: 320, height: 740 },
  ]) for (const surface of ['map', 'board']) {
    const { mobile, width, height } = profile;
    const result = { surface, viewport: `${mobile ? 'mobile' : 'desktop'} ${width}x${height}`, status: 'running' };
    report.cases.push(result);
    const context = await browser.newContext({ viewport: { width, height },
      isMobile: mobile, hasTouch: mobile });
    let page;
    try {
      page = await context.newPage();
      page.setDefaultTimeout(10_000);
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(new URL('test/visual/map-interaction-fixture.html?surface=' + surface, report.listener).href);
      const viewport = page.locator(surface === 'map' ? '.world-map-canvas' : '.tactical-board-viewport');
      const stage = page.locator(surface === 'map' ? '.world-map-stage' : '.tactical-board-stage');
      const zoom = page.getByRole('status', { name: surface === 'map' ? 'Current map zoom' : 'Current tactical board zoom' });
      const toolbar = page.getByRole('toolbar');
      const zoomIn = toolbar.getByRole('button', { name: surface === 'map' ? 'Zoom in' : 'Zoom tactical board in', exact: true });
      const zoomOut = toolbar.getByRole('button', { name: surface === 'map' ? 'Zoom out' : 'Zoom tactical board out', exact: true });
      const reset = toolbar.getByRole('button', { name: 'Reset view', exact: true });
      const fit = toolbar.getByRole('button', { name: surface === 'map' ? 'Fit map' : 'Fit board', exact: true });
      await viewport.waitFor();
      const center = async () => {
        await viewport.evaluate(element => element.scrollIntoView({ block: 'center', behavior: 'instant' }));
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        const bounds = await viewport.boundingBox();
        assert.ok(bounds);
        return { x: bounds.x + bounds.width * .25, y: bounds.y + bounds.height * .5 };
      };
      const point = await center();
      const beforeScroll = await page.evaluate(() => scrollY);
      await page.mouse.move(point.x, point.y);
      await page.mouse.wheel(0, 180);
      await page.waitForFunction(before => scrollY > before, beforeScroll);
      assert.equal(await zoom.textContent(), '100%');
      result.nativeWheelScroll = true;

      await zoomIn.click();
      await viewport.focus();
      for (const key of ['+', '-', '0', 'f', 'F']) {
        await page.keyboard.press(key);
        assert.equal(await zoom.textContent(), '125%', key + ' must not change zoom');
      }
      const beforePan = await stage.getAttribute('style');
      await page.keyboard.press('ArrowRight');
      assert.notEqual(await stage.getAttribute('style'), beforePan, 'Arrow panning must remain available');
      assert.equal(await zoom.textContent(), '125%');
      // Keyboard activation of visible controls remains accessible.
      await zoomIn.focus();
      await page.keyboard.press('Enter');
      assert.equal(await zoom.textContent(), '150%');
      await zoomOut.focus();
      await page.keyboard.press('Space');
      assert.equal(await zoom.textContent(), '125%');
      const outline = await zoomOut.evaluate(element => ({
        style: getComputedStyle(element).outlineStyle, width: parseFloat(getComputedStyle(element).outlineWidth),
      }));
      assert.equal(outline.style, 'solid');
      assert.ok(outline.width >= 2);
      for (let i = 0; i < 20 && !await zoomIn.isDisabled(); i++) await zoomIn.click();
      assert.equal(await zoom.textContent(), '400%');
      assert.ok(await zoomIn.isDisabled());
      for (let i = 0; i < 20 && !await zoomOut.isDisabled(); i++) await zoomOut.click();
      assert.equal(await zoom.textContent(), '50%');
      assert.ok(await zoomOut.isDisabled());
      await fit.click();
      assert.ok(parseInt(await zoom.textContent()) >= 50 && parseInt(await zoom.textContent()) <= 100);
      await reset.click();
      assert.equal(await zoom.textContent(), '100%');
      await zoomIn.click();
      const drag = await center();
      const beforeDrag = await stage.getAttribute('style');
      await page.mouse.move(drag.x, drag.y);
      await page.mouse.down();
      await page.mouse.move(drag.x - 50, drag.y - 30, { steps: 5 });
      await page.mouse.up();
      assert.notEqual(await stage.getAttribute('style'), beforeDrag);
      assert.equal(await zoom.textContent(), '125%');
      if (surface === 'map') {
        const marker = page.locator('[data-feature-id="feature.keep"]');
        await marker.click();
        assert.equal(await marker.getAttribute('aria-pressed'), 'true');
        await toolbar.getByRole('button', { name: 'Focus selected', exact: true }).click();
        assert.equal(await zoom.textContent(), '200%');
        const blank = await center();
        await page.mouse.click(blank.x, blank.y);
        assert.equal(await marker.getAttribute('aria-pressed'), 'false');
        assert.ok(await toolbar.getByRole('button', { name: 'Focus selected', exact: true }).isDisabled());
      }
      const controls = await toolbar.getByRole('button').evaluateAll(buttons => buttons.map(button => {
        const r = button.getBoundingClientRect();
        return { label: button.getAttribute('aria-label') ?? button.textContent.trim(), width: r.width, height: r.height, left: r.left, right: r.right };
      }));
      for (const control of controls) {
        assert.ok(control.label && control.width >= 44 && control.height >= 44);
        assert.ok(control.left >= 0 && control.right <= width, 'Controls must fit the viewport');
      }
      result.controls = controls;
      if (mobile) {
        await reset.click();
        const touch = await center();
        const client = await context.newCDPSession(page);
        const beforeTouch = await page.evaluate(() => scrollY);
        await client.send('Input.dispatchTouchEvent', { type: 'touchStart',
          touchPoints: [{ id: 1, x: touch.x, y: touch.y + 60 }] });
        for (let step = 1; step <= 8; step++) {
          await client.send('Input.dispatchTouchEvent', { type: 'touchMove',
            touchPoints: [{ id: 1, x: touch.x, y: touch.y + 60 - step * 15 }] });
          await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
        }
        await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        result.touchObservation = await page.evaluate(before => ({ before, after: scrollY, scale: visualViewport.scale,
          height: document.documentElement.scrollHeight, client: innerHeight }), beforeTouch);
        await page.waitForFunction(before => scrollY > before, beforeTouch);
        assert.equal(await zoom.textContent(), '100%');
        const pinch = await center();
        const points = distance => [
          { id: 1, x: pinch.x - distance, y: pinch.y },
          { id: 2, x: pinch.x + distance, y: pinch.y },
        ];
        await client.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: points(15) });
        for (let step = 1; step <= 8; step++) {
          await client.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: points(15 + step * 6) });
          await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
        }
        await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await page.waitForFunction(() => visualViewport.scale > 1);
        assert.equal(await zoom.textContent(), '100%', 'Native pinch may magnify the browser, never the map zoom');
        result.nativeTouchScroll = true;
        result.nativeBrowserPinch = true;
        await client.detach();
      }
      assert.deepEqual(errors, []);
      result.status = 'passed';
      console.log(JSON.stringify(result));
    } catch (error) {
      result.status = 'failed'; result.failure = error.message;
      await mkdir(dirname(options.output), { recursive: true });
      await page?.screenshot({ path: resolve(dirname(options.output), `${surface}-${width}-failure.png`) });
      throw error;
    }
    finally { await context.close(); }
  }
} finally {
  await browser?.close();
  await server.close();
  await mkdir(dirname(options.output), { recursive: true });
  await writeFile(options.output, JSON.stringify(report, null, 2) + '\n');
}
