import path from "node:path"
import { fileURLToPath } from "node:url"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"
import { radiantDocGen } from "./plugins/radiantDocGen.ts"

const siteRoot = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(siteRoot, "../..")

export default defineConfig(({ command }) => ({
  base: process.env.DOCS_BASE_PATH || "/",
  plugins: [radiantDocGen({ repoRoot, outDir: path.join(siteRoot, "public", "generated") }), react(), tailwindcss()],
  resolve: { alias: { "@": path.join(siteRoot, "src") } },
  // Local checkout path enables "Open in VS Code" links during development only.
  define: { __REPO_ROOT__: JSON.stringify(command === "serve" ? repoRoot : "") },
  server: { port: 5180, fs: { allow: [repoRoot] } },
  preview: { port: 5180 },
  // The entry bundles every docs/*.md guide for instant navigation and search (~250 kB gzip).
  build: { chunkSizeWarningLimit: 1024 },
}))
