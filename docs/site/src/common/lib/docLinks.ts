/** Where a documentation link points once resolved. */
export type DocLink =
  | { type: "type"; typeId: string; hash?: string }
  | { type: "guide"; slug: string; hash?: string }
  | { type: "hash"; hash: string }
  | { type: "external"; href: string }

/** Resolves a DocGen ref: a type ID, "TypeId#anchor", or an absolute URL. */
export function resolveApiRef(ref: string): DocLink {
  if (/^https?:/.test(ref)) return { type: "external", href: ref }
  const hashIndex = ref.indexOf("#")
  return hashIndex < 0 ? { type: "type", typeId: ref } : { type: "type", typeId: ref.slice(0, hashIndex), hash: ref.slice(hashIndex + 1) }
}

/** Markdown produced by DocGen uses xref: for links into the API reference. */
export function resolveXref(href: string): DocLink | null {
  return href.startsWith("xref:") ? resolveApiRef(href.slice("xref:".length)) : null
}
