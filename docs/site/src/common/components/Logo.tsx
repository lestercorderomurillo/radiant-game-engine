import { cn } from "@/common/lib/utils"

/** Four-facet Radiant mark from radiant-splash-mix-v1.png, traced for small UI sizes. */
export function Logo({ className }: { className?: string }) {
  return (
    <svg
      viewBox="620 185 290 285"
      aria-hidden="true"
      fill="currentColor"
      className={cn("size-6 shrink-0", className)}
    >
      <path d="M758 193 747 309 688 323Z M766 254 783 311 902 322Z M841 332 782 350 771 462Z M627 333 746 346 761 403Z" />
    </svg>
  )
}
