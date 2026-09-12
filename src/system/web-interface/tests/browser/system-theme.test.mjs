import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const {JSDOM} = require('jsdom');
const themeSource = await readFile(new URL('../../../../../DantesRoleplay.Web/BrowserComponents/system-theme.js', import.meta.url), 'utf8');

function mountTheme({storedPreference = null, dark = false, blockedStorage = false} = {}) {
  const dom = new JSDOM('<!doctype html><head></head><body></body>', {
    url: 'https://system.example.test/', runScripts: 'outside-only'
  });
  let storageWrites = 0;
  if (blockedStorage) {
    Object.defineProperty(dom.window, 'localStorage', {configurable: true, value: {
      getItem() { throw new dom.window.DOMException('Storage is blocked.', 'SecurityError'); },
      setItem() { storageWrites++; throw new dom.window.DOMException('Storage is blocked.', 'SecurityError'); }
    }});
  } else if (storedPreference !== null) dom.window.localStorage.setItem('dantes.system-theme.v1', storedPreference);
  const listeners = new Set();
  const query = {
    get matches() { return dark; },
    addEventListener(type, listener) { if (type === 'change') listeners.add(listener); },
    removeEventListener(type, listener) { if (type === 'change') listeners.delete(listener); }
  };
  dom.window.matchMedia = () => query;
  dom.window.eval(`${themeSource.replaceAll('export ', '')}\nwindow.__systemTheme = {getSystemTheme, initializeSystemTheme, onSystemThemeChange, setSystemTheme};`);
  return {
    dom,
    theme: dom.window.__systemTheme,
    get storageWrites() { return storageWrites; },
    setDark(value) {
      dark = value;
      for (const listener of listeners) listener({matches: dark});
    },
    storage(value, key = 'dantes.system-theme.v1') {
      const event = new dom.window.Event('storage');
      Object.defineProperties(event, {
        key: {value: key},
        newValue: {value}
      });
      dom.window.dispatchEvent(event);
    }
  };
}

function contrastRatio(first, second) {
  const luminance = value => {
    const channels = value.slice(1).match(/../g).map(channel => Number.parseInt(channel, 16) / 255);
    const linear = channels.map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
    return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
  };
  const [bright, dark] = [luminance(first), luminance(second)].sort((left, right) => right - left);
  return (bright + 0.05) / (dark + 0.05);
}

function greenWoodToken(name) {
  const block = themeSource.match(/:root\[data-system-theme='green-wood'\]\s*{(?<tokens>[^}]*)}/s)?.groups?.tokens;
  assert.ok(block, 'Green & Wood token block must exist.');
  const value = block.match(new RegExp(`--system-color-${name}:\\s*(#[0-9a-f]{6})`, 'i'))?.[1];
  assert.ok(value, `Green & Wood ${name} token must exist.`);
  return value;
}

test('green and wood is the accessible default for absent, invalid, cleared, and blocked storage', () => {
  for (const options of [{}, {storedPreference: 'sepia'}, {blockedStorage: true}]) {
    const mounted = mountTheme(options);
    try {
      assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'green-wood', resolvedTheme: 'dark'});
      assert.equal(mounted.dom.window.document.documentElement.dataset.systemTheme, 'green-wood');
      assert.equal(mounted.dom.window.document.documentElement.style.colorScheme, 'dark');
      const toggle = mounted.dom.window.document.createElement('system-theme-toggle');
      mounted.dom.window.document.body.append(toggle);
      const optionElements = [...toggle.shadowRoot.querySelectorAll('option')];
      assert.deepEqual(optionElements.map(option => [option.value, option.textContent]), [
        ['green-wood', 'Green & Wood'], ['system', 'System'], ['light', 'Light'], ['dark', 'Dark']
      ]);
    } finally { mounted.dom.window.close(); }
  }

  const mounted = mountTheme({storedPreference: 'dark'});
  try {
    mounted.storage(null, null);
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'green-wood', resolvedTheme: 'dark'});
    assert.deepEqual({...mounted.theme.setSystemTheme('green-wood')}, {preference: 'green-wood', resolvedTheme: 'dark'});
    assert.equal(mounted.dom.window.localStorage.getItem('dantes.system-theme.v1'), 'green-wood');
  } finally { mounted.dom.window.close(); }

  const canvas = greenWoodToken('canvas');
  const surface = greenWoodToken('surface');
  const text = greenWoodToken('text');
  const muted = greenWoodToken('muted');
  const border = greenWoodToken('border');
  const accent = greenWoodToken('accent');
  const accentContrast = greenWoodToken('accent-contrast');
  const danger = greenWoodToken('danger');
  const focus = greenWoodToken('focus');
  assert.equal(canvas.toLowerCase(), '#07100e');
  assert.equal(accent.toLowerCase(), '#c99b52');
  assert.ok(contrastRatio(text, canvas) >= 4.5);
  assert.ok(contrastRatio(text, surface) >= 4.5);
  assert.ok(contrastRatio(muted, surface) >= 4.5);
  assert.ok(contrastRatio(border, surface) >= 3);
  assert.ok(contrastRatio(accentContrast, accent) >= 4.5);
  assert.ok(contrastRatio(danger, surface) >= 4.5);
  assert.ok(contrastRatio(focus, canvas) >= 3);
});

test('system theme persists explicit choices, follows system light and dark, and accepts external storage changes', () => {
  const mounted = mountTheme({storedPreference: 'light', dark: true});
  const changes = [];
  const stop = mounted.theme.onSystemThemeChange(value => changes.push(value));
  try {
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'light', resolvedTheme: 'light'});
    assert.equal(mounted.dom.window.document.documentElement.dataset.systemTheme, 'light');
    assert.equal(mounted.dom.window.document.documentElement.style.colorScheme, 'light');
    assert.equal(mounted.dom.window.document.querySelectorAll('#dantes-system-theme').length, 1);

    assert.deepEqual({...mounted.theme.setSystemTheme('system')}, {preference: 'system', resolvedTheme: 'dark'});
    assert.equal(mounted.dom.window.localStorage.getItem('dantes.system-theme.v1'), 'system');
    mounted.setDark(false);
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'system', resolvedTheme: 'light'});
    assert.equal(mounted.dom.window.document.documentElement.style.colorScheme, 'light');

    mounted.storage('dark');
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'dark', resolvedTheme: 'dark'});
    assert.equal(mounted.dom.window.document.documentElement.dataset.systemTheme, 'dark');
    assert.throws(() => mounted.theme.setSystemTheme('sepia'), error => error?.name === 'TypeError');
    assert.deepEqual(changes.map(value => ({...value})), [
      {preference: 'light', resolvedTheme: 'light'},
      {preference: 'system', resolvedTheme: 'dark'},
      {preference: 'system', resolvedTheme: 'light'},
      {preference: 'dark', resolvedTheme: 'dark'}
    ]);
  } finally {
    stop();
    mounted.dom.window.close();
  }
});

test('system theme still applies an explicit choice when local storage is blocked', () => {
  const mounted = mountTheme({dark: true, blockedStorage: true});
  try {
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'green-wood', resolvedTheme: 'dark'});
    assert.deepEqual({...mounted.theme.setSystemTheme('light')}, {preference: 'light', resolvedTheme: 'light'});
    assert.equal(mounted.storageWrites, 1);
    assert.equal(mounted.dom.window.document.documentElement.dataset.systemTheme, 'light');
    assert.equal(mounted.dom.window.document.documentElement.style.colorScheme, 'light');
  } finally { mounted.dom.window.close(); }
});
