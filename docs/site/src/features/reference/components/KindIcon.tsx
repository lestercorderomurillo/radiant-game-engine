import { Badge } from "@/common/ui/badge"
import { t } from "@/common/i18n/t"
import { cn } from "@/common/lib/utils"
import type { MemberKind, TypeKind } from "../reference.api"

type Kind = TypeKind | MemberKind

const styles: Record<Kind, { letter: string; className: string }> = {
  class: { letter: "C", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  struct: { letter: "S", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  interface: { letter: "I", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  enum: { letter: "E", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  delegate: { letter: "D", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  constructor: { letter: "N", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  method: { letter: "M", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  property: { letter: "P", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  indexer: { letter: "[]", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  field: { letter: "F", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  constant: { letter: "K", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  event: { letter: "Ev", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  operator: { letter: "Op", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
  value: { letter: "V", className: "bg-neutral-500/15 text-neutral-700 dark:text-neutral-300" },
}

/** Small lettered square identifying a symbol kind, like editor outline icons. */
export function KindIcon({ kind, className }: { kind: Kind; className?: string }) {
  const style = styles[kind] ?? styles.class
  return (
    <span
      aria-hidden="true"
      className={cn("inline-flex size-[18px] shrink-0 items-center justify-center rounded-[4px] font-mono text-[9.5px] font-semibold leading-none", style.className, className)}
    >
      {style.letter}
    </span>
  )
}

export function KindBadge({ kind, className }: { kind: Kind; className?: string }) {
  const style = styles[kind] ?? styles.class
  return (
    <Badge variant="outline" className={cn("border-transparent font-mono", style.className, className)}>
      {t(`kind.${kind}`)}
    </Badge>
  )
}
