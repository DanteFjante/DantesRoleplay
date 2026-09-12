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
    storage(value) {
      const event = new dom.window.Event('storage');
      Object.defineProperties(event, {
        key: {value: 'dantes.system-theme.v1'},
        newValue: {value}
      });
      dom.window.dispatchEvent(event);
    }
  };
}

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
    assert.deepEqual({...mounted.theme.getSystemTheme()}, {preference: 'system', resolvedTheme: 'dark'});
    assert.deepEqual({...mounted.theme.setSystemTheme('light')}, {preference: 'light', resolvedTheme: 'light'});
    assert.equal(mounted.storageWrites, 1);
    assert.equal(mounted.dom.window.document.documentElement.dataset.systemTheme, 'light');
    assert.equal(mounted.dom.window.document.documentElement.style.colorScheme, 'light');
  } finally { mounted.dom.window.close(); }
});
