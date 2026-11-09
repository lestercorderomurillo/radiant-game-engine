import type * as React from "react"
import { Link } from "@tanstack/react-router"
import type { DocLink } from "@/common/lib/docLinks"

interface DocAnchorProps extends Omit<React.ComponentProps<"a">, "href"> {
  link: DocLink
}

/** Renders a resolved documentation link as a router link, in-page anchor or external link. */
export function DocAnchor({ link, children, ...props }: DocAnchorProps) {
  switch (link.type) {
    case "type":
      return (
        <Link to="/api/$typeId" params={{ typeId: link.typeId }} hash={link.hash} {...props}>
          {children}
        </Link>
      )
    case "guide":
      return (
        <Link to="/guides/$slug" params={{ slug: link.slug }} hash={link.hash} {...props}>
          {children}
        </Link>
      )
    case "hash":
      return (
        <a href={`#${link.hash}`} {...props}>
          {children}
        </a>
      )
    case "external":
      return (
        <a href={link.href} target="_blank" rel="noreferrer" {...props}>
          {children}
        </a>
      )
  }
}
