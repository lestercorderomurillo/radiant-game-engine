import { Link, useRouterState } from "@tanstack/react-router"
import { MenuIcon, SearchIcon } from "lucide-react"
import { GitHubIcon } from "@/common/components/GitHubIcon"
import { Logo } from "@/common/components/Logo"
import { ThemeToggle } from "@/common/components/ThemeToggle"
import { Badge } from "@/common/ui/badge"
import { Button } from "@/common/ui/button"
import { Kbd } from "@/common/ui/kbd"
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from "@/common/ui/sheet"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"
import { orderedGuides } from "@/features/guides/guides"
import { useApiIndex } from "@/features/reference/useApiIndex"
import { useSearch } from "@/features/search/SearchProvider"
import { SidebarNav } from "./SidebarNav"

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform)

export function SiteHeader() {
  const index = useApiIndex()
  const { setOpen } = useSearch()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const firstGuide = orderedGuides[0]?.slug ?? "overview"

  const navClass = (active: boolean) => cn("rounded-md px-3 py-1.5 transition-colors hover:text-foreground", active ? "text-foreground" : "text-muted-foreground")

  return (
    <header className="sticky top-0 z-40 w-full border-b bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/70">
      <div className="mx-auto flex h-14 w-full max-w-[96rem] items-center gap-2 px-4 md:px-6">
        <MobileNav />

        <Link to="/" className="mr-4 flex items-center gap-2">
          <Logo />
          <span className="font-semibold tracking-tight">{t("site.name")}</span>
          <span className="hidden text-muted-foreground sm:inline">{t("site.docs")}</span>
          {index?.meta.version && (
            <Badge variant="secondary" className="hidden font-mono sm:inline-flex">
              v{index.meta.version}
            </Badge>
          )}
        </Link>

        <nav className="hidden items-center gap-1 text-sm md:flex">
          <Link to="/guides/$slug" params={{ slug: firstGuide }} className={navClass(pathname.startsWith("/guides"))}>
            {t("nav.guides")}
          </Link>
          <Link to="/api" className={navClass(pathname.startsWith("/api"))}>
            {t("nav.api")}
          </Link>
        </nav>

        <div className="ml-auto flex items-center gap-1">
          <Button
            variant="outline"
            onClick={() => setOpen(true)}
            className="hidden h-8 w-64 justify-between rounded-md bg-muted/50 px-3 font-normal text-muted-foreground shadow-none sm:flex"
          >
            <span className="flex items-center gap-2">
              <SearchIcon className="size-3.5" />
              {t("search.open")}
            </span>
            <Kbd>{isMac ? "⌘" : "Ctrl"} K</Kbd>
          </Button>
          <Button variant="ghost" size="icon-sm" className="sm:hidden" aria-label={t("search.title")} onClick={() => setOpen(true)}>
            <SearchIcon />
          </Button>
          {index?.meta.repository?.url && (
            <Button asChild variant="ghost" size="icon-sm">
              <a href={index.meta.repository.url} target="_blank" rel="noreferrer" aria-label={t("nav.github")} title={t("nav.github")}>
                <GitHubIcon className="size-4" />
              </a>
            </Button>
          )}
          <ThemeToggle />
        </div>
      </div>
    </header>
  )
}

function MobileNav() {
  return (
    <Sheet>
      <SheetTrigger asChild>
        <Button variant="ghost" size="icon-sm" className="-ml-2 md:hidden" aria-label={t("nav.openMenu")}>
          <MenuIcon />
        </Button>
      </SheetTrigger>
      <SheetContent>
        <SheetTitle className="flex h-14 items-center gap-2 border-b px-4">
          <Logo />
          {t("site.name")}
        </SheetTitle>
        <SidebarNav />
      </SheetContent>
    </Sheet>
  )
}
