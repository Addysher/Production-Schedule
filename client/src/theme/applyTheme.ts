import type { SettingDto } from '../types/api'

export type ThemeMode = 'system' | 'light' | 'dark'

export const DEFAULT_PRIMARY_COLOR = '#0a5e73'
export const DEFAULT_SECONDARY_COLOR = '#fdb913'
export const DEFAULT_THEME_MODE: ThemeMode = 'system'

const OVERRIDE_KEY = 'themeOverride'

export function readThemeSettings(settings: SettingDto[]) {
  const byKey = Object.fromEntries(settings.map((s) => [s.key, s.value]))
  const mode = byKey.ThemeMode
  return {
    primaryColor: byKey.ThemePrimaryColor || DEFAULT_PRIMARY_COLOR,
    secondaryColor: byKey.ThemeSecondaryColor || DEFAULT_SECONDARY_COLOR,
    mode: (mode === 'light' || mode === 'dark' || mode === 'system' ? mode : DEFAULT_THEME_MODE) as ThemeMode,
  }
}

// null = this browser has never touched the header toggle — defer to the admin's default (Settings ->
// Appearance), same as before the toggle existed. Once the user picks ANY of the three options
// (including Auto), it's stored literally and from then on always means exactly that, independent of
// whatever the admin picks — a user choosing Auto on their own device is asking for their own OS
// setting, not "whatever the sitewide default resolves to".
function readOverride(): ThemeMode | null {
  try {
    const v = localStorage.getItem(OVERRIDE_KEY)
    if (v === 'light' || v === 'dark' || v === 'system') return v
  } catch {
    // no storage: behave as if nothing has ever been chosen
  }
  return null
}

function writeOverride(mode: ThemeMode) {
  try {
    localStorage.setItem(OVERRIDE_KEY, mode)
  } catch {
    // no storage: the toggle still applies for this page view, it just won't persist on reload
  }
}

// Used when an admin changes the sitewide default in Settings -> Appearance: drops this browser's
// own choice (if any) back to "never touched", so their own edit is what they immediately see, rather
// than a stale personal pick silently fighting it.
function clearOverride() {
  try {
    localStorage.removeItem(OVERRIDE_KEY)
  } catch {
    // no storage: there was nothing to clear
  }
}

// Applies just the personal override (if any) immediately, before the authenticated shell has a
// chance to fetch Settings — covers the login page and the first paint of the authenticated shell,
// so a user's own light/dark choice sticks even where there's no admin default to merge it with yet.
export function applyStoredOverride() {
  const override = readOverride()
  if (override !== null) applyMode(override)
}

const THEME_CHANGED_EVENT = 'theme-changed'

// So the header toggle (Layout) reflects a theme change made elsewhere — e.g. an admin changing the
// default in Settings -> Appearance in the same tab — without needing a shared React context.
export function onThemeChanged(listener: () => void) {
  window.addEventListener(THEME_CHANGED_EVENT, listener)
  return () => window.removeEventListener(THEME_CHANGED_EVENT, listener)
}

function applyMode(mode: ThemeMode) {
  const root = document.documentElement
  if (mode === 'system') {
    delete root.dataset.theme
  } else {
    root.dataset.theme = mode
  }
  window.dispatchEvent(new Event(THEME_CHANGED_EVENT))
}

// Applies the two configurable brand colours and the light/dark/system mode to the live document.
// Colours are set as plain CSS custom properties — every derived shade (sidebar background, hover
// state, badge tint) is computed from these two by theme.css's color-mix() rules, not here.
// Mode: this browser's own explicit choice (header toggle) wins if one has ever been made — even
// 'system', which (unlike the untouched case) always means literal OS-follow from then on. Only an
// untouched browser defers to the admin default, which may itself be 'system' (clears data-theme,
// same CSS media-query fallback) or a sitewide pin.
export function applyTheme(settings: SettingDto[]) {
  const { primaryColor, secondaryColor, mode } = readThemeSettings(settings)
  const root = document.documentElement
  root.style.setProperty('--brand-primary', primaryColor)
  root.style.setProperty('--brand-accent', secondaryColor)
  applyMode(readOverride() ?? mode)
}

// What the header toggle should display: the browser's own explicit choice, or 'system' ("Auto") if
// it has never been touched — both read as "Auto" to the user, they just resolve differently above.
export function getThemeOverride(): ThemeMode {
  return readOverride() ?? 'system'
}

// See clearOverride(). Used when an admin changes the default there, so their own choice is what they
// see — a stale personal override would otherwise silently keep overriding it, same as the header
// toggle would for anyone else.
export function clearThemeOverride() {
  clearOverride()
}

// Cycles the PERSONAL (per-browser) choice: Auto -> Light -> Dark -> Auto. This never touches the
// admin-configured Settings row (PUT /api/settings is Admin-only) — it lets any signed-in user,
// Viewer included, flip their own view without write access to Settings. Every click writes an
// explicit choice (even 'system'), which is what makes Auto subsequently mean literal OS-follow
// rather than silently reverting to deferring at the next page load — see readOverride().
export function cycleThemeOverride(): ThemeMode {
  const order: ThemeMode[] = ['system', 'light', 'dark']
  const current = readOverride() ?? 'system'
  const next = order[(order.indexOf(current) + 1) % order.length]
  writeOverride(next)
  applyMode(next)
  return next
}
