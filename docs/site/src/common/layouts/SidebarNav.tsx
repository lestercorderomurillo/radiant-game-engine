import { useEffect, useMemo, useRef, useState, type RefObject } from "react"
import { Link, useRouterState } from "@tanstack/react-router"
import { ChevronRightIcon, ListFilterIcon } from "lucide-react"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/common/ui/collapsible"
import { Input } from "@/common/ui/input"
import { ScrollArea } from "@/common/ui/scroll-area"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"
import { guideAliases } from "@/features/guides/guide-slug.mjs"
import { guideSections } from "@/features/guides/guides"
import { KindIcon } from "@/features/reference/components/KindIcon"
import type { ApiTypeSummary } from "@/features/reference/reference.api"
import { useApiIndex, useTypesById } from "@/features/reference/useApiIndex"

type Tab = "guides" | "api"

const linkClass = (active: boolean) =>
  cn(
    "flex h-8 items-center gap-2 rounded-md px-2 text-sm transition-colors",
    active ? "bg-accent font-medium text-accent-foreground" : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
  )

/** Guides and API navigation; the active tab follows the current route. */
export function SidebarNav() {
  const pathname = useRouterState({ select: (state) => {
    const base = import.meta.env.BASE_URL.replace(/\/$/, "")
    const path = state.location.pathname
    const relative = (base && path.startsWith(base + "/") ? path.slice(base.length) : path).replace(/\/+$/, "") || "/"
    const alias = relative.startsWith("/guides/") ? guideAliases[relative.slice("/guides/".length)] : undefined
    return alias ? `/guides/${alias}` : relative
  } })
  const [tab, setTab] = useState<Tab>(pathname.startsWith("/api") ? "api" : "guides")
  const viewport = useRef<HTMLDivElement>(null)

  useEffect(() => setTab(pathname.startsWith("/api") ? "api" : "guides"), [pathname])

  useEffect(() => {
    viewport.current?.querySelector("[data-active=true]")?.scrollIntoView({ block: "center" })
  }, [tab])

  return (
    <div className="flex h-full flex-col">
      <div className="p-3 pb-2">
        <div className="grid grid-cols-2 rounded-lg bg-muted p-0.5 text-sm" role="tablist">
          {(["guides", "api"] as const).map((value) => (
            <button
              key={value}
              role="tab"
              aria-selected={tab === value}
              onClick={() => setTab(value)}
              className={cn("rounded-md py-1 font-medium transition-colors", tab === value ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground")}
            >
              {t(value === "guides" ? "nav.guides" : "nav.api")}
            </button>
          ))}
        </div>
      </div>
      {tab === "guides" ? (
        <ScrollArea className="min-h-0 flex-1" viewportRef={viewport}>
          <GuidesNav pathname={pathname} />
        </ScrollArea>
      ) : (
        <ApiNav pathname={pathname} viewportRef={viewport} />
      )}
    </div>
  )
}

function GuidesNav({ pathname }: { pathname: string }) {
  return (
    <nav className="flex flex-col gap-5 px-3 pt-2 pb-8">
      {guideSections.map((section) => (
        <div key={section.title}>
          <p className="mb-1 px-2 text-xs font-medium text-muted-foreground/80">{t(section.title)}</p>
          {section.guides.map((guide) => {
            const active = pathname === `/guides/${guide.slug}`
            return (
              <Link key={guide.slug} to="/guides/$slug" params={{ slug: guide.slug }} className={linkClass(active)} data-active={active}>
                <span className="truncate">{guide.title}</span>
              </Link>
            )
          })}
        </div>
      ))}
    </nav>
  )
}

function ApiNav({ pathname, viewportRef }: { pathname: string; viewportRef: RefObject<HTMLDivElement | null> }) {
  const index = useApiIndex()
  const typesById = useTypesById(index)
  const [filter, setFilter] = useState("")
  const query = filter.trim().toLowerCase()
  const activeTypeId = pathname.startsWith("/api/") ? decodeURIComponent(pathname.slice("/api/".length)) : null

  const groups = useMemo(
    () =>
      (index?.groups ?? [])
        .map((group) => ({
          ...group,
          areas: group.areas
            .map((area) => ({ ...area, types: area.types.map((id) => typesById.get(id)).filter((type): type is ApiTypeSummary => !!type && (!query || type.name.toLowerCase().includes(query))) }))
            .filter((area) => area.types.length > 0),
        }))
        .filter((group) => group.areas.length > 0),
    [index, typesById, query],
  )

  return (
    <>
      <div className="relative px-3 pb-2">
        <ListFilterIcon className="pointer-events-none absolute top-2 left-5.5 size-4 text-muted-foreground" />
        <Input value={filter} onChange={(event) => setFilter(event.target.value)} placeholder={t("nav.filterTypes")} className="h-8 pl-8 text-sm" />
      </div>
      <ScrollArea className="min-h-0 flex-1" viewportRef={viewportRef}>
        <nav className="flex flex-col gap-1 px-3 pb-8">
          <Link to="/api" className={linkClass(pathname === "/api")} data-active={pathname === "/api"}>
            {t("reference.title")}
          </Link>
          {query && groups.length === 0 && <p className="px-2 py-4 text-sm text-muted-foreground">{t("nav.noTypes", { query: filter })}</p>}
          {groups.map((group) => {
            const containsActive = group.areas.some((area) => area.types.some((type) => type.id === activeTypeId))
            return (
              <Collapsible key={`${group.id}:${query ? "filtered" : "all"}`} defaultOpen={!!query || containsActive || group.id === "core"}>
                <CollapsibleTrigger className="group flex h-8 w-full items-center gap-1.5 rounded-md px-2 text-sm font-medium hover:bg-accent/50">
                  <ChevronRightIcon className="size-3.5 text-muted-foreground transition-transform group-data-[state=open]:rotate-90" />
                  {group.title}
                  <span className="ml-auto text-xs font-normal text-muted-foreground">{group.areas.reduce((total, area) => total + area.types.length, 0)}</span>
                </CollapsibleTrigger>
                <CollapsibleContent className="ml-3.5 border-l pl-2">
                  {group.areas.map((area) => (
                    <div key={area.id} className="py-1">
                      {area.title && <p className="px-2 pt-1 pb-0.5 text-[11px] font-medium tracking-wide text-muted-foreground/80 uppercase">{area.title}</p>}
                      {area.types.map((type) => {
                        const active = type.id === activeTypeId
                        return (
                          <Link key={type.id} to="/api/$typeId" params={{ typeId: type.id }} className={cn(linkClass(active), "h-7")} data-active={active}>
                            <KindIcon kind={type.kind} />
                            <span className="truncate font-mono text-[13px]">{type.name}</span>
                          </Link>
                        )
                      })}
                    </div>
                  ))}
                </CollapsibleContent>
              </Collapsible>
            )
          })}
        </nav>
      </ScrollArea>
    </>
  )
}
