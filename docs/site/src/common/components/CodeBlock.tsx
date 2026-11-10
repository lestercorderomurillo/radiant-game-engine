import { useEffect, useState } from "react"
import { CopyButton } from "@/common/components/CopyButton"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"

interface CodeBlockProps {
  code: string
  language?: string
  className?: string
}

/** Fenced code with Shiki highlighting (light and dark), a language label and a copy button. */
export function CodeBlock({ code, language, className }: CodeBlockProps) {
  const [html, setHtml] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    // Shiki loads on the first code block so pages without code stay light.
    import("@/common/lib/highlighter")
      .then(({ highlight }) => highlight(code, language))
      .then((result) => !cancelled && setHtml(result))
    return () => {
      cancelled = true
    }
  }, [code, language])

  return (
    <div className={cn("not-prose radiant-code group relative my-5 overflow-hidden rounded-[6px]", className)}>
      <div className="radiant-code__bar flex h-9 items-center justify-between px-3 font-mono text-[12px]">
        <span>{language ?? "text"}</span>
        <CopyButton text={code} label={t("code.copy")} copiedLabel={t("code.copied")} className="-mr-1.5" />
      </div>
      {html ? (
        <div className="overflow-x-auto px-4 py-3 font-mono text-[13px] leading-relaxed scrollbar-thin [&_pre]:outline-none" dangerouslySetInnerHTML={{ __html: html }} />
      ) : (
        <pre className="overflow-x-auto px-4 py-3 font-mono text-[13px] leading-relaxed text-[#e8dcdf]">
          <code>{code}</code>
        </pre>
      )}
    </div>
  )
}
