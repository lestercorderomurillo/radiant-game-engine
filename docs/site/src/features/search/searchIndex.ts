import type { Guide } from "@/features/guides/guides"
import type { ApiSearchEntry, ApiTypeSummary } from "@/features/reference/reference.api"

export interface GuideResult {
  guide: Guide
  headingId?: string
  text: string
  score: number
}

/** Scores a candidate against every query term; 0 means at least one term did not match. */
function score(name: string, detail: string | undefined, terms: string[]) {
  const lowerName = name.toLowerCase()
  const lowerDetail = detail?.toLowerCase() ?? ""
  let total = 0

  for (const term of terms) {
    if (lowerName === term) total += 100
    else if (lowerName.startsWith(term)) total += 70 - Math.min(lowerName.length - term.length, 30)
    else if (lowerName.includes(term)) total += 40 - Math.min(lowerName.indexOf(term), 20)
    else if (lowerDetail.includes(term)) total += 10
    else return 0
  }

  return total
}

const termsOf = (query: string) => query.toLowerCase().split(/\s+/).filter(Boolean)

const top = <T extends { score: number }>(items: T[], limit: number) => items.filter((item) => item.score > 0).sort((left, right) => right.score - left.score).slice(0, limit)

export function searchGuides(guides: Guide[], query: string, limit = 6): GuideResult[] {
  const terms = termsOf(query)
  const results: GuideResult[] = []

  for (const guide of guides) {
    results.push({ guide, text: guide.title, score: score(guide.title, guide.description, terms) * 1.2 })
    for (const heading of guide.headings) results.push({ guide, headingId: heading.id, text: heading.text, score: score(heading.text, guide.title, terms) * 0.8 })
  }

  return top(results, limit)
}

export function searchTypes(types: ApiTypeSummary[], query: string, limit = 8) {
  const terms = termsOf(query)
  return top(
    types.map((type) => ({ type, score: score(type.name, type.summary, terms) })),
    limit,
  )
}

/** Matches members; "Type.Member" narrows to members of matching types. */
export function searchMembers(entries: ApiSearchEntry[], typesById: Map<string, ApiTypeSummary>, query: string, limit = 20) {
  const dot = query.lastIndexOf(".")
  const typeTerm = dot > 0 ? query.slice(0, dot).toLowerCase() : null
  const terms = termsOf(dot > 0 ? query.slice(dot + 1) : query)
  if (terms.length === 0 && !typeTerm) return []

  return top(
    entries.map((entry) => {
      const typeName = typesById.get(entry.t)?.name ?? entry.t
      if (typeTerm && !typeName.toLowerCase().includes(typeTerm)) return { entry, typeName, score: 0 }
      return { entry, typeName, score: terms.length === 0 ? 1 : score(entry.n, entry.s, terms) }
    }),
    limit,
  )
}
