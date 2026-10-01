import type { SettingDto } from '../types/api'

export type ThemeMode = 'system' | 'light' | 'dark'

export const DEFAULT_PRIMARY_COLOR = '#0a5e73'
export const DEFAULT_SECONDARY_COLOR = '#fdb913'
export const DEFAULT_THEME_MODE: ThemeMode = 'system'

export function readThemeSettings(settings: SettingDto[]) {
  const byKey = Object.fromEntries(settings.map((s) => [s.key, s.value]))
  const mode = byKey.ThemeMode
  return {
    primaryColor: byKey.ThemePrimaryColor || DEFAULT_PRIMARY_COLOR,
    secondaryColor: byKey.ThemeSecondaryColor || DEFAULT_SECONDARY_COLOR,
    mode: (mode === 'light' || mode === 'dark' || mode === 'system' ? mode : DEFAULT_THEME_MODE) as ThemeMode,
  }
}

// Applies the two configurable brand colours and the light/dark/system mode to the live document.
// Colours are set as plain CSS custom properties — every derived shade (sidebar background, hover
// state, badge tint) is computed from these two by theme.css's color-mix() rules, not here.
// Mode 'system' clears data-theme entirely so the stylesheet's prefers-color-scheme block decides —
// matching the OS live, with no JS listener needed.
export function applyTheme(settings: SettingDto[]) {
  const { primaryColor, secondaryColor, mode } = readThemeSettings(settings)
  const root = document.documentElement
  root.style.setProperty('--brand-primary', primaryColor)
  root.style.setProperty('--brand-accent', secondaryColor)
  if (mode === 'system') {
    delete root.dataset.theme
  } else {
    root.dataset.theme = mode
  }
}
