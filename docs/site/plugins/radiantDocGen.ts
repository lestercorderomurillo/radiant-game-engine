import { spawn } from "node:child_process"
import path from "node:path"
import type { Plugin } from "vite"

/** Custom HMR event the client listens to after regenerated API JSON lands on disk. */
export const apiUpdatedEvent = "radiant:api-updated"

interface RadiantDocGenOptions {
  repoRoot: string
  outDir: string
}

/**
 * Runs tools/Radiant.DocGen before the site starts or builds, and again whenever
 * engine C# sources change during `vite dev`.
 */
export function radiantDocGen({ repoRoot, outDir }: RadiantDocGenOptions): Plugin {
  const projectPath = path.join(repoRoot, "radiant.csproj")
  const toolProject = path.join(repoRoot, "tools", "Radiant.DocGen")
  const toolAssembly = path.join(toolProject, "bin", "Release", "net8.0", "Radiant.DocGen.dll")
  // Project files and sources anywhere under src/ and eng/, so project splits are picked up too.
  const watchedPaths = [path.join(repoRoot, "src"), path.join(repoRoot, "eng"), projectPath]

  let isBuild = false
  let toolBuilt = false
  let running: Promise<boolean> | null = null
  let rerunRequested = false

  const run = (command: string, args: string[]) =>
    new Promise<boolean>((resolve) => {
      const child = spawn(command, args, { cwd: repoRoot, stdio: "inherit" })
      child.on("close", (code) => resolve(code === 0))
      child.on("error", () => resolve(false))
    })

  async function generateOnce() {
    if (!toolBuilt) toolBuilt = await run("dotnet", ["build", toolProject, "-c", "Release", "--nologo", "-v", "quiet", "-clp:ErrorsOnly"])
    if (!toolBuilt) return false
    return run("dotnet", [toolAssembly, "--project", projectPath, "--out", outDir])
  }

  /** Serializes runs; changes that arrive mid-run trigger exactly one follow-up run. */
  async function generate(): Promise<boolean> {
    if (running) {
      rerunRequested = true
      return running
    }

    running = (async () => {
      let succeeded = await generateOnce()
      while (rerunRequested) {
        rerunRequested = false
        succeeded = await generateOnce()
      }
      return succeeded
    })()

    try {
      return await running
    } finally {
      running = null
    }
  }

  return {
    name: "radiant-docgen",

    configResolved(config) {
      isBuild = config.command === "build"
    },

    async buildStart() {
      if (await generate()) return
      const message = "Radiant.DocGen failed; is the .NET SDK installed? API pages will be empty."
      if (isBuild) this.error(message)
      else this.warn(message)
    },

    configureServer(server) {
      let timer: ReturnType<typeof setTimeout> | undefined
      server.watcher.add(watchedPaths)

      server.watcher.on("all", (_event, file) => {
        if (!/\.(cs|csproj|props)$/.test(file) || /[\\/](bin|obj)[\\/]/.test(file) || !watchedPaths.some((watched) => file.startsWith(watched))) return

        clearTimeout(timer)
        timer = setTimeout(async () => {
          if (await generate()) server.environments.client.hot.send({ type: "custom", event: apiUpdatedEvent })
        }, 250)
      })
    },
  }
}
