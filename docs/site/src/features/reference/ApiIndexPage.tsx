import { useEffect, useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { ListFilterIcon } from "lucide-react"
import { DocPage } from "@/common/layouts/DocPage"
import { Input } from "@/common/ui/input"
import { useDebounced } from "@/common/hooks/useDebounced"
import { t } from "@/common/i18n/t"
import { KindIcon } from "./components/KindIcon"
import type { ApiTypeSummary } from "./reference.api"
import { useApiIndex, useTypesById } from "./useApiIndex"

const matches = (type: ApiTypeSummary, query: string) => !query || type.name.toLowerCase().includes(query) || !!type.summary?.toLowerCase().includes(query)

export function ApiIndexPage() {
  const index = useApiIndex()
  const typesById = useTypesById(index)
  const [filter, setFilter] = useState("")
  const query = useDebounced(filter.trim().toLowerCase(), 250)

  const groups = useMemo(
    () =>
      (index?.groups ?? [])
        .map((group) => ({
          ...group,
          areas: group.areas
            .map((area) => ({ ...area, types: area.types.map((id) => typesById.get(id)).filter((type): type is ApiTypeSummary => !!type && matches(type, query)) }))
            .filter((area) => area.types.length > 0),
        }))
        .filter((group) => group.areas.length > 0),
    [index, typesById, query],
  )

  // Scroll a group into view when arriving from the sidebar with #group.
  useEffect(() => {
    const id = decodeURIComponent(window.location.hash.slice(1))
    if (id) document.getElementById(id)?.scrollIntoView()
  }, [])

  if (!index) {
    return (
      <DocPage toc={[]}>
        <h1 className="text-3xl font-semibold tracking-tight">{t("reference.title")}</h1>
        <p className="mt-4 text-muted-foreground">{t("home.noData")}</p>
      </DocPage>
    )
  }

  const memberCount = index.types.reduce((total, type) => total + (type.members ?? 0), 0)

  return (
    <DocPage toc={groups.map((group) => ({ id: group.id, text: group.title, depth: 2 }))}>
      <h1 className="text-3xl font-semibold tracking-tight">{t("reference.title")}</h1>
      <p className="mt-3 text-lg text-muted-foreground">{t("reference.description", { types: index.types.length, members: memberCount })}</p>

      <div className="relative mt-6 max-w-md">
        <ListFilterIcon className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
        <Input value={filter} onChange={(event) => setFilter(event.target.value)} placeholder={t("reference.filter")} className="pl-8" />
      </div>

      {groups.length === 0 && <p className="mt-8 text-sm text-muted-foreground">{t("reference.noMatches", { query: filter })}</p>}

      {groups.map((group) => (
        <section key={group.id} id={group.id} className="mt-12 scroll-mt-20">
          <h2 className="border-b pb-2 text-2xl font-semibold tracking-tight">{group.title}</h2>
          {group.areas.map((area) => (
            <div key={area.id} className="mt-6">
              {area.title && <h3 className="mb-3 text-sm font-medium tracking-wide text-muted-foreground uppercase">{area.title}</h3>}
              <div className="grid gap-3 sm:grid-cols-2">
                {area.types.map((type) => (
                  <Link
                    key={type.id}
                    to="/api/$typeId"
                    params={{ typeId: type.id }}
                    className="group flex flex-col gap-1.5 rounded-lg border p-3 transition-colors hover:border-foreground/20 hover:bg-accent/50"
                  >
                    <span className="flex items-center gap-2">
                      <KindIcon kind={type.kind} />
                      <span className="truncate font-mono text-sm font-medium">{type.name}</span>
                      {type.members ? <span className="ml-auto shrink-0 text-xs text-muted-foreground">{t("reference.members", { count: type.members })}</span> : null}
                    </span>
                    {type.summary && <span className="line-clamp-2 text-sm text-muted-foreground">{type.summary}</span>}
                  </Link>
                ))}
              </div>
            </div>
          ))}
        </section>
      ))}
    </DocPage>
  )
}
