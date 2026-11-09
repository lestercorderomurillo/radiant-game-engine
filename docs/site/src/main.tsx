import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { RouterProvider } from "@tanstack/react-router"
import { router } from "@/common/router/router"
import { clearApiCache } from "@/features/reference/reference.api"
import "@/common/styles/globals.css"

// Sent by plugins/radiantDocGen.ts after C# changes were regenerated.
if (import.meta.hot) {
  import.meta.hot.on("radiant:api-updated", () => {
    clearApiCache()
    router.invalidate()
  })
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
