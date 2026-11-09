import type { ReactNode } from "react"
import { TableOfContents, type TocItem } from "@/common/components/TableOfContents"

/** Article column plus the sticky "On this page" rail on wide screens. */
export function DocPage({ toc, children }: { toc: TocItem[]; children: ReactNode }) {
  return (
    <div className="flex gap-10 md:pl-8 lg:pl-12">
      <article id="content" className="min-w-0 flex-1 py-8 lg:py-10">
        <div className="mx-auto max-w-3xl">{children}</div>
      </article>
      <aside className="sticky top-14 hidden h-[calc(100svh-3.5rem)] w-60 shrink-0 overflow-y-auto py-10 pr-2 scrollbar-thin xl:block">
        <TableOfContents items={toc} />
      </aside>
    </div>
  )
}
