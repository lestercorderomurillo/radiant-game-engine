import { SquareCodeIcon } from "lucide-react"
import { GitHubIcon } from "@/common/components/GitHubIcon"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"
import { editorUrl, sourceUrl, type ApiMeta, type ApiSource } from "../reference.api"

/** "path:line" linking to GitHub, plus a VS Code shortcut while running locally. */
export function SourceLinks({ meta, source, className }: { meta?: ApiMeta; source: ApiSource; className?: string }) {
  const remote = sourceUrl(meta, source)
  const local = editorUrl(source)
  const label = `${source.path.split("/").pop()}:${source.line}`

  return (
    <span className={cn("inline-flex items-center gap-2 text-xs text-muted-foreground", className)}>
      {remote ? (
        <a href={remote} target="_blank" rel="noreferrer" title={source.path} className="inline-flex items-center gap-1.5 font-mono hover:text-foreground">
          <GitHubIcon className="size-3.5" />
          {label}
        </a>
      ) : (
        <span className="font-mono" title={source.path}>
          {label}
        </span>
      )}
      {local && (
        <a href={local} title={t("reference.openInEditor")} aria-label={t("reference.openInEditor")} className="hover:text-foreground">
          <SquareCodeIcon className="size-3.5" />
        </a>
      )}
    </span>
  )
}
