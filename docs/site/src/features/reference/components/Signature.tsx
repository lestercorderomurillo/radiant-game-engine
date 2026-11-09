import { CopyButton } from "@/common/components/CopyButton"
import { DocAnchor } from "@/common/components/DocAnchor"
import { t } from "@/common/i18n/t"
import { resolveApiRef } from "@/common/lib/docLinks"
import { cn } from "@/common/lib/utils"
import type { Token } from "../reference.api"

export const tokensToText = (tokens: Token[]) => tokens.map(([, text]) => text).join("")

/** Inline C# tokens; type and member names link to their pages. */
export function Tokens({ tokens, className }: { tokens: Token[]; className?: string }) {
  return (
    <code className={cn("font-mono", className)}>
      {tokens.map(([kind, text, ref], index) =>
        ref ? (
          <DocAnchor key={index} link={resolveApiRef(ref)} className={cn(`sx-${kind}`, "underline-offset-2 hover:underline")}>
            {text}
          </DocAnchor>
        ) : (
          <span key={index} className={`sx-${kind}`}>
            {text}
          </span>
        ),
      )}
    </code>
  )
}

/** Declaration panel with wrapping, linked tokens and a copy button. */
export function SignatureBlock({ tokens, className }: { tokens: Token[]; className?: string }) {
  return (
    <div className={cn("group relative rounded-lg border bg-code", className)}>
      <pre className="overflow-x-auto py-3 pr-11 pl-4 text-[13px] leading-relaxed whitespace-pre-wrap break-words scrollbar-thin">
        <Tokens tokens={tokens} />
      </pre>
      <CopyButton text={tokensToText(tokens)} label={t("reference.copy")} copiedLabel={t("reference.copied")} className="absolute top-2 right-2 opacity-60 group-hover:opacity-100" />
    </div>
  )
}
