import { useCallback, useEffect, useState } from "react"

export type ThemePreference = "light" | "dark" | "system"

const storageKey = "radiant-docs-theme"
const darkQuery = "(prefers-color-scheme: dark)"

function readPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(storageKey)
    return stored === "light" || stored === "dark" || stored === "system" ? stored : "dark"
  } catch {
    return "dark"
  }
}

function applyPreference(preference: ThemePreference) {
  const dark = preference === "dark" || (preference === "system" && matchMedia(darkQuery).matches)
  document.documentElement.classList.toggle("dark", dark)
}

/** Light/dark/system preference stored per browser; index.html applies it before first paint. */
export function useTheme() {
  const [preference, setPreferenceState] = useState<ThemePreference>(readPreference)

  useEffect(() => {
    applyPreference(preference)
    if (preference !== "system") return

    const media = matchMedia(darkQuery)
    const onChange = () => applyPreference("system")
    media.addEventListener("change", onChange)
    return () => media.removeEventListener("change", onChange)
  }, [preference])

  const setPreference = useCallback((next: ThemePreference) => {
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Storage can be unavailable (private mode); the choice still applies for this visit.
    }
    setPreferenceState(next)
  }, [])

  return { preference, setPreference }
}
