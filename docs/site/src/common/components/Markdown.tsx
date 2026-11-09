import { isValidElement, type ReactElement, type ReactNode } from "react"
import ReactMarkdown, { defaultUrlTransform, type Components } from "react-markdown"
import rehypeSlug from "rehype-slug"
import remarkGfm from "remark-gfm"
import { CodeBlock } from "@/common/components/CodeBlock"
import { DocAnchor } from "@/common/components/DocAnchor"
import { resolveXref, type DocLink } from "@/common/lib/docLinks"
import { cn } from "@/common/lib/utils"

interface MarkdownProps {
  source: string
  /** Resolves links that are not xref: links, e.g. relative paths inside a guide. */
  resolveLink?: (href: string) => DocLink
  resolveImage?: (src: string) => string
  /** Full prose for guides, compact prose for API summaries, large muted text for a page lead. */
  variant?: "guide" | "compact" | "lead"
  className?: string
}

const fallbackLink = (href: string): DocLink => (href.startsWith("#") ? { type: "hash", hash: href.slice(1) } : { type: "external", href })

export function Markdown({ source, resolveLink = fallbackLink, resolveImage, variant = "guide", className }: MarkdownProps) {
  const components: Components = {
    a: ({ href = "", children }) => (
      <DocAnchor link={resolveXref(href) ?? resolveLink(href)} className="font-medium underline decoration-muted-foreground/40 underline-offset-4 hover:decoration-foreground">
        {children}
      </DocAnchor>
    ),
    pre: ({ children }) => {
      const code = isValidElement(children) ? (children as ReactElement<{ className?: string; children?: ReactNode }>) : null
      const language = /language-([\w#+-]+)/.exec(code?.props.className ?? "")?.[1]
      return <CodeBlock code={String(code?.props.children ?? "").replace(/\n$/, "")} language={language} />
    },
    img: ({ src = "", alt }) => (
      <img src={resolveImage && typeof src === "string" ? resolveImage(src) : src} alt={alt ?? ""} loading="lazy" className="rounded-lg border" />
    ),
    table: ({ children }) => (
      <div className="not-prose my-6 w-full overflow-x-auto rounded-lg border">
        <table className="w-full text-sm [&_td]:border-t [&_td]:px-3 [&_td]:py-2 [&_th]:bg-muted/50 [&_th]:px-3 [&_th]:py-2 [&_th]:text-left [&_th]:font-medium">{children}</table>
      </div>
    ),
  }

  return (
    <div
      className={cn(
        "prose prose-neutral max-w-none dark:prose-invert",
        variant !== "compact" && "prose-p:max-w-[68ch]",
        "prose-headings:scroll-mt-20 prose-headings:font-semibold prose-headings:tracking-tight prose-h2:mt-12 prose-h2:border-b prose-h2:pb-2 prose-h2:text-2xl prose-h3:text-lg",
        "prose-code:rounded-md prose-code:bg-muted prose-code:px-1.5 prose-code:py-0.5 prose-code:text-[0.85em] prose-code:font-normal prose-code:before:content-none prose-code:after:content-none",
        "prose-a:font-medium prose-strong:font-semibold prose-li:my-1 prose-img:my-6",
        variant === "compact" && "prose-sm prose-p:my-2 prose-p:leading-relaxed first:prose-p:mt-0 last:prose-p:mb-0 prose-ul:my-2",
        variant === "lead" && "prose-lg prose-p:my-0 prose-p:text-muted-foreground prose-code:text-[0.8em]",
        className,
      )}
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={variant === "guide" ? [rehypeSlug] : []}
        urlTransform={(url) => (url.startsWith("xref:") ? url : defaultUrlTransform(url))}
        components={components}
      >
        {source}
      </ReactMarkdown>
    </div>
  )
}
