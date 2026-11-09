import { DocAnchor } from "@/common/components/DocAnchor"
import { resolveApiRef } from "@/common/lib/docLinks"
import type { ApiLink } from "../reference.api"

const chipClass = "inline-flex items-center rounded-md border bg-background px-2 py-0.5 font-mono text-xs transition-colors"

/** Wrapping list of linked type or member names. */
export function LinkChips({ links }: { links: ApiLink[] }) {
  return (
    <div className="flex flex-wrap gap-1.5">
      {links.map((link) =>
        link.ref ? (
          <DocAnchor key={link.text + link.ref} link={resolveApiRef(link.ref)} className={`${chipClass} hover:border-foreground/30 hover:bg-accent`}>
            {link.text}
          </DocAnchor>
        ) : (
          <span key={link.text} className={`${chipClass} text-muted-foreground`}>
            {link.text}
          </span>
        ),
      )}
    </div>
  )
}
