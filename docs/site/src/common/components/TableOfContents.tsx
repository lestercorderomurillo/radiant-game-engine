import { useActiveHeading } from "@/common/hooks/useActiveHeading"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"

export interface TocItem {
  id: string
  text: string
  depth: number
}

/** Sticky "On this page" list with the current section highlighted. */
export function TableOfContents({ items }: { items: TocItem[] }) {
  const activeId = useActiveHeading(items.map((item) => item.id))
  if (items.length === 0) return null

  return (
    <nav aria-label={t("toc.title")} className="flex flex-col gap-2 text-sm">
      <p className="font-medium">{t("toc.title")}</p>
      <ul className="flex flex-col gap-0.5 border-l">
        {items.map((item) => (
          <li key={item.id}>
            <a
              href={`#${item.id}`}
              className={cn(
                "-ml-px block break-words border-l py-1 leading-relaxed text-muted-foreground transition-colors hover:text-foreground",
                item.depth <= 2 ? "pl-3" : "pl-6 text-[13px]",
                activeId === item.id ? "border-brand font-medium text-foreground" : "border-transparent",
              )}
            >
              {item.text}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  )
}
