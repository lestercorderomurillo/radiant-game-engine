import { Outlet } from "@tanstack/react-router"
import { SidebarNav } from "./SidebarNav"

/** Sidebar navigation beside guide and API pages. */
export function DocsLayout() {
  return (
    <div className="mx-auto flex w-full max-w-[96rem] px-4 md:px-6">
      <aside className="sticky top-14 hidden h-[calc(100svh-3.5rem)] w-64 shrink-0 border-r md:block">
        <SidebarNav />
      </aside>
      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  )
}
