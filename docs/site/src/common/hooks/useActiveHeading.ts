import { useEffect, useState } from "react"

/** Tracks which heading is nearest the top of the viewport while scrolling. */
export function useActiveHeading(ids: string[]) {
  const [activeId, setActiveId] = useState<string | null>(null)
  const key = ids.join("|")

  useEffect(() => {
    if (ids.length === 0) return

    const update = () => {
      let current: string | null = ids[0]
      for (const id of ids) {
        const element = document.getElementById(id)
        if (!element) continue
        if (element.getBoundingClientRect().top - 96 <= 0) current = id
        else break
      }
      setActiveId(current)
    }

    update()
    window.addEventListener("scroll", update, { passive: true })
    window.addEventListener("resize", update)
    return () => {
      window.removeEventListener("scroll", update)
      window.removeEventListener("resize", update)
    }
    // Keyed by the joined IDs so a new array with the same headings does not resubscribe.
  }, [key])

  return activeId
}
