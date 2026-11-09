import { readmeForGuide } from "./readme-guide.mjs"
import { slugFromFile } from "./guide-slug.mjs"
import GithubSlugger from "github-slugger"
import { t, type MessageKey } from "@/common/i18n/t"
import type { DocLink } from "@/common/lib/docLinks"
import type { ApiIndex } from "@/features/reference/reference.api"

/** Every Markdown file in docs/ plus the repository README, loaded as raw text (HMR on edit). */
const docsSources = import.meta.glob<string>("../../../../*.md", { query: "?raw", import: "default", eager: true })
const readmeSources = import.meta.glob<string>("../../../../../README.md", { query: "?raw", import: "default", eager: true })
const imageUrls = import.meta.glob<string>("../../../../images/*", { query: "?url", import: "default", eager: true })

export interface GuideHeading {
  id: string
  text: string
  depth: number
}

export interface Guide {
  slug: string
  title: string
  /** Plain-text lead used for search; lead keeps its Markdown for the page header. */
  description: string
  lead: string
  /** Repository-relative path, e.g. docs/Rendering.md. */
  path: string
  /** Markdown without its leading H1, which the page renders as the title. */
  body: string
  headings: GuideHeading[]
}

export interface GuideSection {
  title: MessageKey
  guides: Guide[]
}

/** Sidebar order. Markdown files not listed here are appended under "More" automatically. */
const sectionOrder: { title: MessageKey; slugs: string[] }[] = [
  { title: "guides.sections.start", slugs: ["overview", "getting-started"] },
  { title: "guides.sections.engine", slugs: ["ecs", "systems", "input", "inspector", "camera-controls", "math"] },
  {
    title: "guides.sections.rendering",
    slugs: ["renderer", "hrc", "udr4", "rendering", "rendering-pipeline", "3d", "graphics-resources", "render-states", "compute-shaders"],
  },
  { title: "guides.sections.platform", slugs: ["runtime-backends", "renderer-contracts"] },
]

/** Text content of inline Markdown, matching what rehype-slug sees when it assigns heading IDs. */
const inlineText = (markdown: string) =>
  markdown
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/[`*_~]/g, "")
    .trim()

function parseGuide(path: string, source: string, slug: string, titleOverride?: string): Guide {
  const lines = source.replace(/\r\n/g, "\n").split("\n")
  const titleIndex = lines.findIndex((line) => /^#\s+/.test(line))
  const title = titleOverride ?? (titleIndex >= 0 ? inlineText(lines[titleIndex].replace(/^#\s+/, "")) : slug)
  let bodyLines = titleIndex >= 0 ? lines.slice(titleIndex + 1) : lines

  // A plain paragraph directly under the title becomes the page lead instead of repeating in the body.
  const leadStart = bodyLines.findIndex((line) => line.trim().length > 0)
  let lead = ""
  if (leadStart >= 0 && !/^\s*([#!|<>`-]|\d+\.)/.test(bodyLines[leadStart])) {
    let leadEnd = leadStart
    while (leadEnd < bodyLines.length && bodyLines[leadEnd].trim().length > 0) leadEnd++
    lead = bodyLines.slice(leadStart, leadEnd).join("\n")
    bodyLines = bodyLines.slice(leadEnd)
  }

  const slugger = new GithubSlugger()
  const headings: GuideHeading[] = []
  let inFence = false

  for (const line of bodyLines) {
    if (line.startsWith("```")) inFence = !inFence
    if (inFence) continue

    const heading = /^(#{1,6})\s+(.+?)\s*#*$/.exec(line)
    if (heading) {
      const text = inlineText(heading[2])
      headings.push({ id: slugger.slug(text), text, depth: heading[1].length })
    }
  }

  return { slug, title, description: inlineText(lead.replace(/\n/g, " ")), lead, path, body: bodyLines.join("\n").trim(), headings }
}

const allGuides: Guide[] = [
  ...Object.entries(readmeSources).map(([, source]) => parseGuide("README.md", readmeForGuide(source), "overview", t("guides.overviewTitle"))),
  ...Object.entries(docsSources).map(([file, source]) => {
    const name = file.split("/").pop()!
    return parseGuide(`docs/${name}`, source, slugFromFile(name))
  }),
]

const guidesBySlug = new Map(allGuides.map((guide) => [guide.slug, guide]))
const guidesByPath = new Map(allGuides.map((guide) => [guide.path.toLowerCase(), guide]))

export const guideSections: GuideSection[] = (() => {
  const listed = new Set(sectionOrder.flatMap((section) => section.slugs))
  const sections = sectionOrder
    .map((section) => ({ title: section.title, guides: section.slugs.map((slug) => guidesBySlug.get(slug)).filter((guide): guide is Guide => !!guide) }))
    .filter((section) => section.guides.length > 0)
  const unlisted = allGuides.filter((guide) => !listed.has(guide.slug)).sort((left, right) => left.title.localeCompare(right.title))
  return unlisted.length > 0 ? [...sections, { title: "nav.more", guides: unlisted }] : sections
})()

/** Guides in sidebar order, for previous/next navigation. */
export const orderedGuides = guideSections.flatMap((section) => section.guides)

export const getGuide = (slug: string) => guidesBySlug.get(slug)

export const sectionOf = (guide: Guide) => guideSections.find((section) => section.guides.includes(guide))

/** Resolves an image path written relative to a guide (docs/images/x.png or images/x.png). */
export function resolveGuideImage(src: string) {
  if (/^(https?:|data:)/.test(src)) return src
  const name = src.split("/").pop()
  const match = Object.entries(imageUrls).find(([file]) => file.endsWith(`/${name}`))
  return match?.[1] ?? src
}

/**
 * Resolves a link inside a guide: other guides route internally, engine sources map to their
 * API page, and any other repository path opens on GitHub.
 */
export function resolveGuideLink(href: string, guide: Guide, index: ApiIndex | null, sourceTypes: Map<string, string>): DocLink {
  if (href.startsWith("#")) return { type: "hash", hash: href.slice(1) }
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) return { type: "external", href }

  const url = new URL(href, `https://repository.invalid/${guide.path}`)
  const repositoryPath = decodeURIComponent(url.pathname.slice(1))
  const hash = url.hash ? url.hash.slice(1) : undefined

  const target = guidesByPath.get(repositoryPath.toLowerCase())
  if (target) return { type: "guide", slug: target.slug, hash }

  const typeId = sourceTypes.get(repositoryPath)
  if (typeId) return { type: "type", typeId }

  const repository = index?.meta.repository
  const branch = repository?.branch ?? "main"
  return repository?.url ? { type: "external", href: `${repository.url}/blob/${branch}/${repositoryPath}${url.hash}` } : { type: "external", href }
}

export function editUrl(guide: Guide, index: ApiIndex | null) {
  const repository = index?.meta.repository
  return repository?.url ? `${repository.url}/edit/${repository.branch ?? "main"}/${guide.path}` : undefined
}
