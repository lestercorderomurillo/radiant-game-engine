/** The docs layout supplies its own title; GitHub's HTML masthead is repository-only. */
export function readmeForGuide(source) {
  const heading = /^\s*<h1\b[^>]*>[\s\S]*?<\/h1>\s*/i.exec(source)
  if (!heading) return source
  const body = source.slice(heading[0].length)
  const badges = /^<p\b[^>]*>[\s\S]*?<\/p>\s*/i.exec(body)
  return badges?.[0].includes("img.shields.io") ? body.slice(badges[0].length) : body
}
