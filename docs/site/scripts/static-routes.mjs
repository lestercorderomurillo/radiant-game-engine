import { readFile, readdir, mkdir, writeFile } from "node:fs/promises"
import { fileURLToPath } from "node:url"
import path from "node:path"
import { slugFromFile } from "../src/features/guides/guide-slug.mjs"

const site = fileURLToPath(new URL("../", import.meta.url))
const dist = path.join(site, "dist")
const html = await readFile(path.join(dist, "index.html"), "utf8")
const api = JSON.parse(await readFile(path.join(dist, "generated/index.json"), "utf8"))
const guides = (await readdir(path.resolve(site, ".."))).filter(file => file.endsWith(".md"))
const routes = ["api", "guides/overview", ...guides.map(file => `guides/${slugFromFile(file)}`), ...api.types.map(type => `api/${type.id}`)]
for (const route of routes) {
  const directory = path.resolve(dist, route)
  if (!directory.startsWith(dist + path.sep)) throw new Error(`Invalid route: ${route}`)
  await mkdir(directory, { recursive: true })
  await writeFile(path.join(directory, "index.html"), html)
}
// Known routes return HTTP 200; unknown routes retain the site's not-found UI.
await writeFile(path.join(dist, "404.html"), html)
await writeFile(path.join(dist, ".nojekyll"), "")
console.log(`Static entry points: ${routes.length} guides and API routes`)
