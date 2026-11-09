import { Outlet } from "@tanstack/react-router"
import { t } from "@/common/i18n/t"
import { SearchDialog } from "@/features/search/SearchDialog"
import { SearchProvider } from "@/features/search/SearchProvider"
import { SiteHeader } from "./SiteHeader"

export function RootLayout() {
  return (
    <SearchProvider>
      <a href="#content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2">
        {t("site.skipToContent")}
      </a>
      <div className="relative flex min-h-svh flex-col">
        <SiteHeader />
        <main className="flex-1">
          <Outlet />
        </main>
      </div>
      <SearchDialog />
    </SearchProvider>
  )
}
