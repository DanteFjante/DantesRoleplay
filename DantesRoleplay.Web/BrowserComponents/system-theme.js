export const SYSTEM_THEME_STORAGE_KEY = 'dantes.system-theme.v1';
export const SYSTEM_THEME_PREFERENCES = Object.freeze(['green-wood', 'system', 'light', 'dark']);
export const SYSTEM_THEME_EVENT = 'system-theme-change';
const DARK_QUERY = '(prefers-color-scheme: dark)';
const DEFAULT_PREFERENCE = 'green-wood';
const PREFERENCE_LABELS = Object.freeze({
  'green-wood': 'Green & Wood',
  system: 'System',
  light: 'Light',
  dark: 'Dark'
});
let preference = DEFAULT_PREFERENCE;
let mediaQuery = null;
let initialized = false;

function validPreference(value) {
  return SYSTEM_THEME_PREFERENCES.includes(value);
}

function readStoredPreference() {
  try {
    const value = window.localStorage?.getItem(SYSTEM_THEME_STORAGE_KEY);
    return validPreference(value) ? value : DEFAULT_PREFERENCE;
  } catch (_) {
    return DEFAULT_PREFERENCE;
  }
}

function resolvedTheme(value = preference) {
  if (value === 'light' || value === 'dark') return value;
  if (value === 'green-wood') return 'dark';
  return mediaQuery?.matches === true ? 'dark' : 'light';
}

function themeDetail() {
  return Object.freeze({preference, resolvedTheme: resolvedTheme()});
}

function dispatchThemeChange() {
  window.dispatchEvent(new CustomEvent(SYSTEM_THEME_EVENT, {detail: themeDetail()}));
}

function applyTheme(value, notify) {
  preference = validPreference(value) ? value : DEFAULT_PREFERENCE;
  document.documentElement.dataset.systemTheme = preference;
  document.documentElement.style.colorScheme = resolvedTheme();
  if (notify) dispatchThemeChange();
  return themeDetail();
}

function installTokens() {
  if (document.getElementById('dantes-system-theme')) return;
  const style = document.createElement('style');
  style.id = 'dantes-system-theme';
  style.textContent = `
    :root {
      --system-color-canvas: #f3f5f7;
      --system-color-surface: #ffffff;
      --system-color-surface-raised: #f8fafb;
      --system-color-text: #17212b;
      --system-color-muted: #5d6874;
      --system-color-border: #d4dbe1;
      --system-color-accent: #486b7d;
      --system-color-accent-contrast: #ffffff;
      --system-color-danger: #a13d49;
      --system-color-focus: #2f6385;
      --system-radius-small: .5rem;
      --system-radius-medium: .85rem;
      --system-radius-large: 1.25rem;
      --system-shadow-raised: 0 1rem 3rem rgba(31, 43, 53, .12);
      --system-font-sans: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      --system-font-display: Georgia, "Times New Roman", serif;
      color-scheme: light;
    }
    :root[data-system-theme='dark'] {
      --system-color-canvas: #101519;
      --system-color-surface: #171d22;
      --system-color-surface-raised: #1e262c;
      --system-color-text: #edf1f3;
      --system-color-muted: #a5afb7;
      --system-color-border: #39444c;
      --system-color-accent: #8bb3c5;
      --system-color-accent-contrast: #0d171c;
      --system-color-danger: #ff9ca3;
      --system-color-focus: #a9cfe2;
      --system-shadow-raised: 0 1rem 3rem rgba(0, 0, 0, .32);
      color-scheme: dark;
    }
    :root[data-system-theme='green-wood'] {
      --system-color-canvas: #07100e;
      --system-color-surface: #111c19;
      --system-color-surface-raised: #182521;
      --system-color-text: #f0eadb;
      --system-color-muted: #b6b0a1;
      --system-color-border: #8a6b45;
      --system-color-accent: #c99b52;
      --system-color-accent-contrast: #07100e;
      --system-color-danger: #ff9b91;
      --system-color-focus: #e6bd72;
      --system-shadow-raised: 0 1rem 3rem rgba(0, 0, 0, .42);
      color-scheme: dark;
    }
    @media (prefers-color-scheme: dark) {
      :root[data-system-theme='system'] {
        --system-color-canvas: #101519;
        --system-color-surface: #171d22;
        --system-color-surface-raised: #1e262c;
        --system-color-text: #edf1f3;
        --system-color-muted: #a5afb7;
        --system-color-border: #39444c;
        --system-color-accent: #8bb3c5;
        --system-color-accent-contrast: #0d171c;
        --system-color-danger: #ff9ca3;
        --system-color-focus: #a9cfe2;
        --system-shadow-raised: 0 1rem 3rem rgba(0, 0, 0, .32);
        color-scheme: dark;
      }
    }
  `;
  document.head.append(style);
}

export function initializeSystemTheme() {
  if (!initialized) {
    initialized = true;
    installTokens();
    mediaQuery = typeof window.matchMedia === 'function' ? window.matchMedia(DARK_QUERY) : null;
    const mediaChanged = () => {
      if (preference !== 'system') return;
      document.documentElement.style.colorScheme = resolvedTheme();
      dispatchThemeChange();
    };
    if (typeof mediaQuery?.addEventListener === 'function') mediaQuery.addEventListener('change', mediaChanged);
    else if (typeof mediaQuery?.addListener === 'function') mediaQuery.addListener(mediaChanged);
    window.addEventListener('storage', event => {
      if (event.key === SYSTEM_THEME_STORAGE_KEY || event.key === null) {
        applyTheme(validPreference(event.newValue) ? event.newValue : DEFAULT_PREFERENCE, true);
      }
    });
  }
  return applyTheme(readStoredPreference(), false);
}

export function getSystemTheme() {
  if (!initialized) initializeSystemTheme();
  return themeDetail();
}

export function setSystemTheme(value) {
  if (!validPreference(value)) throw new TypeError('Theme preference must be green-wood, system, light, or dark.');
  if (!initialized) initializeSystemTheme();
  try { window.localStorage?.setItem(SYSTEM_THEME_STORAGE_KEY, value); }
  catch (_) { /* The in-memory preference still applies for this page. */ }
  return applyTheme(value, true);
}

export function onSystemThemeChange(listener, options = {}) {
  if (typeof listener !== 'function') throw new TypeError('A theme listener is required.');
  if (!initialized) initializeSystemTheme();
  const handler = event => listener(event.detail);
  window.addEventListener(SYSTEM_THEME_EVENT, handler, options.signal ? {signal: options.signal} : undefined);
  if (options.immediate !== false) listener(themeDetail());
  return () => window.removeEventListener(SYSTEM_THEME_EVENT, handler);
}

export class SystemThemeToggle extends HTMLElement {
  constructor() {
    super();
    this._stop = null;
    this.attachShadow({mode: 'open'});
    const style = document.createElement('style');
    style.textContent = `
      :host { display: inline-flex; color: var(--system-color-muted, inherit); font: inherit; }
      label { align-items: center; display: inline-flex; gap: .45rem; font-size: .78rem; font-weight: 650; }
      select { min-height: 2.25rem; border: 1px solid var(--system-color-border, currentColor); border-radius: var(--system-radius-small, .5rem); background: var(--system-color-surface, Canvas); color: var(--system-color-text, CanvasText); font: inherit; padding: .35rem 1.8rem .35rem .55rem; }
      select:focus-visible { outline: 2px solid var(--system-color-focus, currentColor); outline-offset: 2px; }
    `;
    const label = document.createElement('label');
    label.textContent = 'Theme';
    this._select = document.createElement('select');
    this._select.setAttribute('aria-label', 'Color theme');
    for (const value of SYSTEM_THEME_PREFERENCES) {
      const option = document.createElement('option');
      option.value = value;
      option.textContent = PREFERENCE_LABELS[value];
      this._select.append(option);
    }
    this._select.addEventListener('change', () => setSystemTheme(this._select.value));
    label.append(this._select);
    this.shadowRoot.append(style, label);
  }

  connectedCallback() {
    this._stop?.();
    this._stop = onSystemThemeChange(value => { this._select.value = value.preference; });
  }

  disconnectedCallback() {
    this._stop?.();
    this._stop = null;
  }
}

initializeSystemTheme();
if (!customElements.get('system-theme-toggle')) customElements.define('system-theme-toggle', SystemThemeToggle);
