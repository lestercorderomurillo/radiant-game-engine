import { useEffect, useMemo, useState } from "react"
import { useNavigate } from "@tanstack/react-router"
import { FileTextIcon, HashIcon } from "lucide-react"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/common/ui/command"
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/common/ui/dialog"
import { Kbd } from "@/common/ui/kbd"
import { t } from "@/common/i18n/t"
import { orderedGuides } from "@/features/guides/guides"
import { KindIcon } from "@/features/reference/components/KindIcon"
import { loadApiSearch, type ApiSearchEntry } from "@/features/reference/reference.api"
import { useApiIndex, useTypesById } from "@/features/reference/useApiIndex"
import { useSearch } from "./SearchProvider"
import { searchGuides, searchMembers, searchTypes } from "./searchIndex"

const suggestedTypes = ["ECS", "System", "Scene", "Renderer", "Inspector", "Material"]

export function SearchDialog() {
  const { open, setOpen } = useSearch()
  const navigate = useNavigate()
  const index = useApiIndex()
  const typesById = useTypesById(index)
  const [query, setQuery] = useState("")
  const [members, setMembers] = useState<ApiSearchEntry[] | null>(null)

  useEffect(() => {
    if (open && !members) loadApiSearch().then(setMembers, () => setMembers([]))
    if (!open) setQuery("")
  }, [open, members])

  // Regenerated data replaces the index object; refetch members alongside it.
  useEffect(() => setMembers(null), [index])

  const trimmed = query.trim()
  const results = useMemo(() => {
    if (!trimmed) return null
    return {
      guides: searchGuides(orderedGuides, trimmed),
      types: searchTypes(index?.types ?? [], trimmed),
      members: members ? searchMembers(members, typesById, trimmed) : [],
    }
  }, [trimmed, index, typesById, members])

  const go = (action: () => void) => {
    setOpen(false)
    action()
  }

  const openType = (typeId: string, hash?: string) => go(() => navigate({ to: "/api/$typeId", params: { typeId }, hash }))
  const openGuide = (slug: string, hash?: string) => go(() => navigate({ to: "/guides/$slug", params: { slug }, hash }))

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent showCloseButton={false} className="top-[15%] translate-y-0 gap-0 overflow-hidden p-0 sm:max-w-xl">
        <DialogTitle className="sr-only">{t("search.title")}</DialogTitle>
        <DialogDescription className="sr-only">{t("search.placeholder")}</DialogDescription>
        <Command shouldFilter={false} loop>
          <CommandInput value={query} onValueChange={setQuery} placeholder={t("search.placeholder")} />
          <CommandList>
            {results ? (
              <>
                <CommandEmpty>{members ? t("search.empty", { query: trimmed }) : t("search.loading")}</CommandEmpty>
                {results.types.length > 0 && (
                  <CommandGroup heading={t("search.types")}>
                    {results.types.map(({ type }) => (
                      <CommandItem key={type.id} value={`type:${type.id}`} onSelect={() => openType(type.id)}>
                        <KindIcon kind={type.kind} />
                        <span className="font-mono">{type.name}</span>
                        {type.summary && <span className="truncate text-xs text-muted-foreground">{type.summary}</span>}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
                {results.members.length > 0 && (
                  <CommandGroup heading={t("search.members")}>
                    {results.members.map(({ entry, typeName }) => (
                      <CommandItem key={`${entry.t}#${entry.a}`} value={`member:${entry.t}#${entry.a}`} onSelect={() => openType(entry.t, entry.a)}>
                        <KindIcon kind={entry.k} />
                        <span className="shrink-0 font-mono">
                          <span className="text-muted-foreground">{typeName}.</span>
                          {entry.n}
                        </span>
                        {entry.s && <span className="truncate text-xs text-muted-foreground">{entry.s}</span>}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
                {results.guides.length > 0 && (
                  <CommandGroup heading={t("search.guides")}>
                    {results.guides.map((result) => (
                      <CommandItem
                        key={`${result.guide.slug}#${result.headingId ?? ""}`}
                        value={`guide:${result.guide.slug}#${result.headingId ?? ""}`}
                        onSelect={() => openGuide(result.guide.slug, result.headingId)}
                      >
                        {result.headingId ? <HashIcon className="text-muted-foreground" /> : <FileTextIcon className="text-muted-foreground" />}
                        <span className="truncate">{result.text}</span>
                        {result.headingId && <span className="ml-auto shrink-0 text-xs text-muted-foreground">{result.guide.title}</span>}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                )}
              </>
            ) : (
              <>
                <CommandGroup heading={t("search.guides")}>
                  {orderedGuides.slice(0, 5).map((guide) => (
                    <CommandItem key={guide.slug} value={`guide:${guide.slug}`} onSelect={() => openGuide(guide.slug)}>
                      <FileTextIcon className="text-muted-foreground" />
                      {guide.title}
                    </CommandItem>
                  ))}
                </CommandGroup>
                <CommandGroup heading={t("search.suggested")}>
                  {suggestedTypes
                    .map((id) => typesById.get(id))
                    .filter((type) => !!type)
                    .map((type) => (
                      <CommandItem key={type.id} value={`type:${type.id}`} onSelect={() => openType(type.id)}>
                        <KindIcon kind={type.kind} />
                        <span className="font-mono">{type.name}</span>
                        {type.summary && <span className="truncate text-xs text-muted-foreground">{type.summary}</span>}
                      </CommandItem>
                    ))}
                </CommandGroup>
              </>
            )}
          </CommandList>
          <div className="flex items-center gap-4 border-t px-3 py-2 text-xs text-muted-foreground">
            <span className="flex items-center gap-1">
              <Kbd>↑</Kbd>
              <Kbd>↓</Kbd> {t("search.navigate")}
            </span>
            <span className="flex items-center gap-1">
              <Kbd>↵</Kbd> {t("search.select")}
            </span>
            <span className="flex items-center gap-1">
              <Kbd>Esc</Kbd> {t("search.close")}
            </span>
          </div>
        </Command>
      </DialogContent>
    </Dialog>
  )
}
