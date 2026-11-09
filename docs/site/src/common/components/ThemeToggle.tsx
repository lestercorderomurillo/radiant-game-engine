import { MonitorIcon, MoonIcon, SunIcon } from "lucide-react"
import { Button } from "@/common/ui/button"
import { useTheme, type ThemePreference } from "@/common/hooks/useTheme"
import { t } from "@/common/i18n/t"

const order: ThemePreference[] = ["system", "light", "dark"]
const labels = { system: "theme.system", light: "theme.light", dark: "theme.dark" } as const

/** Cycles system → light → dark. */
export function ThemeToggle() {
  const { preference, setPreference } = useTheme()
  const next = order[(order.indexOf(preference) + 1) % order.length]
  const Icon = preference === "light" ? SunIcon : preference === "dark" ? MoonIcon : MonitorIcon

  return (
    <Button variant="ghost" size="icon-sm" aria-label={`${t("theme.toggle")}: ${t(labels[preference])}`} title={t(labels[preference])} onClick={() => setPreference(next)}>
      <Icon />
    </Button>
  )
}
