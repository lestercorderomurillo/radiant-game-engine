/** Shapes and loaders for the JSON written by tools/Radiant.DocGen into public/generated. */

export type TokenKind = "k" | "t" | "m" | "p" | "n" | "s" | "o" | "w" | "x"
export type Token = [kind: TokenKind, text: string, ref?: string]

export type TypeKind = "class" | "struct" | "interface" | "enum" | "delegate"
export type MemberKind = "constructor" | "field" | "constant" | "property" | "indexer" | "event" | "method" | "operator" | "value"

export interface ApiLink {
  text: string
  ref?: string
}

export interface ApiSource {
  path: string
  line: number
}

export interface ApiRepository {
  url: string
  branch?: string
  commit?: string
}

export interface ApiMeta {
  name: string
  version: string
  generatedAt: string
  repository?: ApiRepository
  namespaces?: string[]
}

export interface ApiArea {
  id: string
  title?: string
  types: string[]
}

export interface ApiGroup {
  id: string
  title: string
  areas: ApiArea[]
}

export interface ApiTypeSummary {
  id: string
  name: string
  kind: TypeKind
  namespace: string
  area: string
  summary?: string
  members?: number
  /** Primary declaration file, used to map guide links to type pages. */
  source?: string
}

export interface ApiIndex {
  meta: ApiMeta
  groups: ApiGroup[]
  types: ApiTypeSummary[]
}

export interface ApiNamedDoc {
  name: string
  description?: string
}

export interface ApiParameter {
  name: string
  type?: Token[]
  modifier?: string
  defaultValue?: string
  description?: string
}

interface ApiDocumented {
  summary?: string
  summaryText?: string
  remarks?: string
  examples?: string[]
  typeParameters?: ApiNamedDoc[]
  obsolete?: string
}

export interface ApiMember extends ApiDocumented {
  id: string
  group: string
  name: string
  kind: MemberKind
  modifiers?: string[]
  signature: Token[]
  parameters?: ApiParameter[]
  returnType?: Token[]
  returns?: string
  value?: string
  constantValue?: string
  exceptions?: { type?: ApiLink; description?: string }[]
  overrides?: ApiLink
  implements?: ApiLink[]
  source?: ApiSource
}

export interface ApiType extends ApiDocumented {
  id: string
  name: string
  fullName: string
  namespace: string
  kind: TypeKind
  area: string
  modifiers?: string[]
  declaration: Token[]
  baseTypes?: ApiLink[]
  interfaces?: ApiLink[]
  derived?: ApiLink[]
  implementations?: ApiLink[]
  nested?: ApiLink[]
  containingType?: ApiLink
  sources?: ApiSource[]
  members?: ApiMember[]
  inherited?: { from: ApiLink; members: ApiLink[] }[]
  extensions?: ApiLink[]
  parameters?: ApiParameter[]
  returns?: string
}

/** Compact search row: t type ID, a anchor, n name, k kind, s summary. */
export interface ApiSearchEntry {
  t: string
  a: string
  n: string
  k: MemberKind
  s?: string
}

const dataRoot = `${import.meta.env.BASE_URL}generated/`

let indexRequest: Promise<ApiIndex | null> | null = null
let searchRequest: Promise<ApiSearchEntry[]> | null = null
const typeRequests = new Map<string, Promise<ApiType | null>>()

async function fetchJson<T>(path: string): Promise<T | null> {
  const response = await fetch(dataRoot + path, { cache: "no-cache" })
  if (!response.ok || !response.headers.get("content-type")?.includes("json")) return null
  return (await response.json()) as T
}

/** Navigation index; resolves to null when DocGen has not produced data yet. */
export function loadApiIndex() {
  indexRequest ??= fetchJson<ApiIndex>("index.json").catch(() => null)
  return indexRequest
}

/** Full documentation for one type, or null when the ID is unknown. */
export function loadApiType(typeId: string) {
  let request = typeRequests.get(typeId)
  if (!request) {
    request = fetchJson<ApiType>(`types/${encodeURIComponent(typeId)}.json`).catch(() => null)
    typeRequests.set(typeId, request)
  }
  return request
}

export function loadApiSearch() {
  searchRequest ??= fetchJson<ApiSearchEntry[]>("search.json").then((entries) => entries ?? [])
  return searchRequest
}

/** Drops cached responses so the next load sees freshly regenerated files. */
export function clearApiCache() {
  indexRequest = null
  searchRequest = null
  typeRequests.clear()
}

/** Browse URL for a declaration in the repository, when the remote is known. */
export function sourceUrl(meta: ApiMeta | undefined, source: ApiSource) {
  const repository = meta?.repository
  if (!repository?.url) return undefined
  return `${repository.url}/blob/${repository.branch ?? "main"}/${source.path}#L${source.line}`
}

declare const __REPO_ROOT__: string

/** vscode:// link to the local checkout; only available while running the dev server. */
export function editorUrl(source: ApiSource) {
  return __REPO_ROOT__ ? `vscode://file/${__REPO_ROOT__}/${source.path}:${source.line}` : undefined
}
