import { getRouteApi, Link } from "@tanstack/react-router"
import { ArrowLeftIcon, ArrowRightIcon, PencilIcon } from "lucide-react"
import { DocPage } from "@/common/layouts/DocPage"
import { Markdown } from "@/common/components/Markdown"
import { t } from "@/common/i18n/t"
import { useApiIndex, useSourceTypes } from "@/features/reference/useApiIndex"
import { editUrl, getGuide, orderedGuides, resolveGuideImage, resolveGuideLink, sectionOf } from "./guides"

const guideRoute = getRouteApi("/docs/guides/$slug")

export function GuidePage() {
  const { slug } = guideRoute.useParams()
  const index = useApiIndex()
  const sourceTypes = useSourceTypes(index)
  const guide = getGuide(slug)

  if (!guide) {
    return (
      <DocPage toc={[]}>
        <p className="text-muted-foreground">{t("guides.notFound")}</p>
      </DocPage>
    )
  }

  const position = orderedGuides.indexOf(guide)
  const previous = orderedGuides[position - 1]
  const next = orderedGuides[position + 1]
  const section = sectionOf(guide)
  const edit = editUrl(guide, index)
  const resolveLink = (href: string) => resolveGuideLink(href, guide, index, sourceTypes)

  return (
    <DocPage toc={guide.headings.filter((heading) => heading.depth === 2 || heading.depth === 3)}>
      {section && <p className="mb-2 text-sm font-medium text-brand">{t(section.title)}</p>}
      <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">{guide.title}</h1>
      {guide.lead && (
        <Markdown
          source={guide.lead}
          resolveLink={resolveLink}
          variant="lead"
          className="mt-3"
        />
      )}

      <Markdown
        key={guide.slug}
        source={guide.body}
        resolveLink={resolveLink}
        resolveImage={resolveGuideImage}
        className="mt-8"
      />

      <div className="mt-12 flex flex-col gap-6 border-t pt-6">
        {edit && (
          <a href={edit} target="_blank" rel="noreferrer" className="inline-flex w-fit items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
            <PencilIcon className="size-3.5" />
            {t("guides.editOnGithub")}
          </a>
        )}
        <div className="grid gap-3 sm:grid-cols-2">
          {previous ? (
            <Link to="/guides/$slug" params={{ slug: previous.slug }} className="flex flex-col gap-1 rounded-lg border p-4 transition-colors hover:bg-accent/50">
              <span className="flex items-center gap-1 text-xs text-muted-foreground">
                <ArrowLeftIcon className="size-3" /> {t("guides.previous")}
              </span>
              <span className="font-medium">{previous.title}</span>
            </Link>
          ) : (
            <span />
          )}
          {next && (
            <Link to="/guides/$slug" params={{ slug: next.slug }} className="flex flex-col items-end gap-1 rounded-lg border p-4 text-right transition-colors hover:bg-accent/50">
              <span className="flex items-center gap-1 text-xs text-muted-foreground">
                {t("guides.next")} <ArrowRightIcon className="size-3" />
              </span>
              <span className="font-medium">{next.title}</span>
            </Link>
          )}
        </div>
      </div>
    </DocPage>
  )
}
