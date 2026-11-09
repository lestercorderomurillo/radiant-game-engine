import { createRootRoute, createRoute, createRouter } from "@tanstack/react-router"
import { DocsLayout } from "@/common/layouts/DocsLayout"
import { NotFoundPage } from "@/common/layouts/NotFoundPage"
import { RootLayout } from "@/common/layouts/RootLayout"
import { GuidePage } from "@/features/guides/GuidePage"
import { HomePage } from "@/features/home/HomePage"
import { ApiIndexPage } from "@/features/reference/ApiIndexPage"
import { loadApiIndex, loadApiType } from "@/features/reference/reference.api"
import { TypePage } from "@/features/reference/TypePage"

const rootRoute = createRootRoute({ loader: loadApiIndex, component: RootLayout, notFoundComponent: NotFoundPage })

const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: "/", component: HomePage })

/** Pathless layout that adds the sidebar to guide and API pages. */
const docsRoute = createRoute({ getParentRoute: () => rootRoute, id: "docs", component: DocsLayout })

const guideRoute = createRoute({ getParentRoute: () => docsRoute, path: "guides/$slug", component: GuidePage })

const referenceRoute = createRoute({ getParentRoute: () => docsRoute, path: "api", component: ApiIndexPage })

const typeRoute = createRoute({
  getParentRoute: () => docsRoute,
  path: "api/$typeId",
  loader: ({ params }) => loadApiType(params.typeId),
  component: TypePage,
})

const routeTree = rootRoute.addChildren([homeRoute, docsRoute.addChildren([guideRoute, referenceRoute, typeRoute])])

export const router = createRouter({
  routeTree,
  basepath: import.meta.env.BASE_URL,
  defaultPreload: "intent",
  defaultPreloadStaleTime: 0,
  scrollRestoration: true,
})

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router
  }
}
