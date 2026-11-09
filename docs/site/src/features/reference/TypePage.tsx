import { useMemo, useState } from "react"
import { getRouteApi, Link } from "@tanstack/react-router"
import { ChevronRightIcon, ListFilterIcon } from "lucide-react"
import { DocPage } from "@/common/layouts/DocPage"
import { Markdown } from "@/common/components/Markdown"
import type { TocItem } from "@/common/components/TableOfContents"
import { Badge } from "@/common/ui/badge"
import { Input } from "@/common/ui/input"
import { t, type MessageKey } from "@/common/i18n/t"
import { KindBadge } from "./components/KindIcon"
import { LinkChips } from "./components/LinkChips"
import { MemberGroup } from "./components/MemberGroup"
import { ParameterList } from "./components/ParameterList"
import { SignatureBlock } from "./components/Signature"
import { SourceLinks } from "./components/SourceLinks"
import type { ApiIndex, ApiLink, ApiMember, ApiType, MemberKind } from "./reference.api"
import { useApiIndex } from "./useApiIndex"

const typeRoute = getRouteApi("/docs/api/$typeId")

const memberSections: { id: string; title: MessageKey; kinds: MemberKind[] }[] = [
  { id: "values", title: "reference.sections.values", kinds: ["value"] },
  { id: "constructors", title: "reference.sections.constructors", kinds: ["constructor"] },
  { id: "fields", title: "reference.sections.fields", kinds: ["constant", "field"] },
  { id: "properties", title: "reference.sections.properties", kinds: ["property", "indexer"] },
  { id: "events", title: "reference.sections.events", kinds: ["event"] },
  { id: "methods", title: "reference.sections.methods", kinds: ["method"] },
  { id: "operators", title: "reference.sections.operators", kinds: ["operator"] },
]

/** Overloads share a group anchor; keep declaration order inside each group. */
function groupMembers(members: ApiMember[]) {
  const groups = new Map<string, ApiMember[]>()
  for (const member of members) groups.set(member.group, [...(groups.get(member.group) ?? []), member])
  return [...groups.entries()].map(([anchor, overloads]) => ({ anchor, overloads }))
}

export function TypePage() {
  const type = typeRoute.useLoaderData()
  const { typeId } = typeRoute.useParams()
  const index = useApiIndex()

  if (!type) {
    return (
      <DocPage toc={[]}>
        <p className="text-muted-foreground">{t("reference.notFound", { id: typeId })}</p>
      </DocPage>
    )
  }

  return <TypeDocumentation key={type.id} type={type} index={index} />
}

function TypeDocumentation({ type, index }: { type: ApiType; index: ApiIndex | null }) {
  const [filter, setFilter] = useState("")
  const query = filter.trim().toLowerCase()

  const sections = useMemo(() => {
    const members = (type.members ?? []).filter((member) => !query || member.name.toLowerCase().includes(query) || member.summaryText?.toLowerCase().includes(query))
    return memberSections
      .map((section) => ({ ...section, groups: groupMembers(members.filter((member) => section.kinds.includes(member.kind))) }))
      .filter((section) => section.groups.length > 0)
  }, [type, query])

  const toc: TocItem[] = [
    { id: "overview", text: t("reference.sections.overview"), depth: 2 },
    ...sections.flatMap((section) => [
      { id: `section-${section.id}`, text: t(section.title), depth: 2 },
      ...(section.id === "values" ? [] : section.groups.map((group) => ({ id: group.anchor, text: group.overloads[0].name, depth: 3 }))),
    ]),
    ...(type.inherited?.length ? [{ id: "inherited", text: t("reference.inheritedMembers"), depth: 2 }] : []),
  ]

  const group = index?.groups.find((candidate) => candidate.areas.some((area) => area.id === type.area))
  const area = group?.areas.find((candidate) => candidate.id === type.area)
  const memberCount = type.members?.length ?? 0
  const relations: { title: MessageKey; links?: ApiLink[] }[] = [
    { title: "reference.inheritance", links: type.baseTypes?.slice().reverse() },
    { title: "reference.implements", links: type.interfaces },
    { title: "reference.derived", links: type.derived },
    { title: "reference.implementations", links: type.implementations },
    { title: "reference.nested", links: type.nested },
    { title: "reference.containingType", links: type.containingType ? [type.containingType] : undefined },
    { title: "reference.extensions", links: type.extensions },
  ]

  return (
    <DocPage toc={toc}>
      <nav className="mb-4 flex items-center gap-1 text-sm text-muted-foreground">
        <Link to="/api" className="hover:text-foreground">
          {t("nav.api")}
        </Link>
        {group && (
          <>
            <ChevronRightIcon className="size-3.5" />
            <span>{group.title}</span>
          </>
        )}
        {area?.title && (
          <>
            <ChevronRightIcon className="size-3.5" />
            <span>{area.title}</span>
          </>
        )}
      </nav>

      <header id="overview" className="scroll-mt-20 space-y-4">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-3xl font-semibold tracking-tight break-all">{type.name}</h1>
          <KindBadge kind={type.kind} />
          {type.modifiers?.map((modifier) => (
            <Badge key={modifier} variant="secondary" className="font-mono">
              {modifier}
            </Badge>
          ))}
          {type.obsolete && (
            <Badge variant="destructive" title={type.obsolete}>
              {t("reference.obsolete")}
            </Badge>
          )}
        </div>

        {type.summary ? <Markdown source={type.summary} variant="compact" className="prose-base text-foreground/90" /> : <p className="text-muted-foreground">{t("reference.noSummary")}</p>}

        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-muted-foreground">
          <span>
            {t("reference.namespace")} <code className="font-mono text-foreground">{type.namespace}</code>
          </span>
          {type.sources?.map((source) => <SourceLinks key={`${source.path}:${source.line}`} meta={index?.meta} source={source} />)}
          {memberCount > 0 && <span>{t("reference.members", { count: memberCount })}</span>}
        </div>
      </header>

      <SignatureBlock tokens={type.declaration} className="mt-6" />

      {type.obsolete && <p className="mt-3 text-sm text-destructive">{type.obsolete}</p>}

      {relations.some((relation) => relation.links?.length) && (
        <dl className="mt-6 grid gap-4 rounded-lg border p-4 sm:grid-cols-2">
          {relations.map((relation) =>
            relation.links?.length ? (
              <div key={relation.title} className="space-y-2">
                <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t(relation.title)}</dt>
                <dd>
                  <LinkChips links={relation.links} />
                </dd>
              </div>
            ) : null,
          )}
        </dl>
      )}

      {type.kind === "delegate" && type.parameters?.some((parameter) => parameter.description) ? (
        <div className="mt-6">
          <ParameterList parameters={type.parameters} />
        </div>
      ) : null}

      {type.kind === "delegate" && type.returns && (
        <div className="mt-6 space-y-1">
          <h4 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{t("reference.returns")}</h4>
          <Markdown source={type.returns} variant="compact" />
        </div>
      )}

      {type.typeParameters?.length ? (
        <div className="mt-6">
          <ParameterList title={t("reference.typeParameters")} parameters={type.typeParameters} />
        </div>
      ) : null}

      {type.remarks && (
        <section className="mt-8">
          <h2 className="mb-2 text-lg font-semibold">{t("reference.remarks")}</h2>
          <Markdown source={type.remarks} variant="compact" />
        </section>
      )}

      {type.examples?.length ? (
        <section className="mt-8">
          <h2 className="mb-2 text-lg font-semibold">{t("reference.examples")}</h2>
          {type.examples.map((example, exampleIndex) => (
            <Markdown key={exampleIndex} source={example} variant="compact" />
          ))}
        </section>
      ) : null}

      {memberCount > 0 && (
        <div className="mt-10 flex items-center gap-2">
          <div className="relative w-full max-w-xs">
            <ListFilterIcon className="pointer-events-none absolute top-2.5 left-2.5 size-4 text-muted-foreground" />
            <Input value={filter} onChange={(event) => setFilter(event.target.value)} placeholder={t("reference.filterMembers")} className="h-9 pl-8" />
          </div>
        </div>
      )}

      {query && sections.length === 0 && <p className="mt-6 text-sm text-muted-foreground">{t("reference.noMemberMatches", { query: filter })}</p>}

      {sections.map((section) => (
        <section key={section.id} id={`section-${section.id}`} className="mt-10 scroll-mt-28">
          <h2 className="border-b pb-2 text-2xl font-semibold tracking-tight">{t(section.title)}</h2>
          {section.id === "values" ? (
            <EnumValues members={section.groups.flatMap((entry) => entry.overloads)} />
          ) : (
            section.groups.map((entry) => <MemberGroup key={entry.anchor} anchor={entry.anchor} overloads={entry.overloads} meta={index?.meta} />)
          )}
        </section>
      ))}

      {type.inherited?.length ? (
        <section id="inherited" className="mt-12 scroll-mt-28 space-y-4">
          <h2 className="border-b pb-2 text-2xl font-semibold tracking-tight">{t("reference.inheritedMembers")}</h2>
          {type.inherited.map((inherited) => (
            <div key={inherited.from.text} className="space-y-2">
              <p className="text-sm text-muted-foreground">{t("reference.inherited", { type: inherited.from.text })}</p>
              <LinkChips links={inherited.members} />
            </div>
          ))}
        </section>
      ) : null}
    </DocPage>
  )
}

function EnumValues({ members }: { members: ApiMember[] }) {
  return (
    <div className="mt-4 overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50 text-left">
          <tr>
            <th className="px-3 py-2 font-medium">{t("reference.name")}</th>
            <th className="px-3 py-2 font-medium">{t("reference.valueColumn")}</th>
            <th className="px-3 py-2 font-medium">{t("reference.description.column")}</th>
          </tr>
        </thead>
        <tbody>
          {[...members]
            .sort((left, right) => Number(left.constantValue) - Number(right.constantValue))
            .map((member) => (
              <tr key={member.id} id={member.id} className="scroll-mt-28 border-t align-top">
                <td className="px-3 py-2 font-mono font-medium">{member.name}</td>
                <td className="px-3 py-2 font-mono text-muted-foreground">{member.constantValue}</td>
                <td className="px-3 py-2">{member.summary && <Markdown source={member.summary} variant="compact" />}</td>
              </tr>
            ))}
        </tbody>
      </table>
    </div>
  )
}
