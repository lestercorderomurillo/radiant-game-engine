import { createHighlighterCore, type HighlighterCore } from "shiki/core"
import { createJavaScriptRegexEngine } from "shiki/engine/javascript"

const languageAliases: Record<string, string> = {
  cs: "csharp",
  "c#": "csharp",
  csharp: "csharp",
  sh: "shellscript",
  bash: "shellscript",
  shell: "shellscript",
  fish: "shellscript",
  hlsl: "hlsl",
  fx: "hlsl",
  json: "json",
  xml: "xml",
  csproj: "xml",
}

let highlighter: Promise<HighlighterCore> | null = null

function getHighlighter() {
  highlighter ??= createHighlighterCore({
    themes: [import("shiki/themes/github-light.mjs"), import("shiki/themes/github-dark.mjs")],
    langs: [
      import("shiki/langs/csharp.mjs"),
      import("shiki/langs/shellscript.mjs"),
      import("shiki/langs/hlsl.mjs"),
      import("shiki/langs/json.mjs"),
      import("shiki/langs/xml.mjs"),
    ],
    engine: createJavaScriptRegexEngine(),
  })
  return highlighter
}

/** Highlights code into dual-theme HTML; unknown languages render as plain escaped text. */
export async function highlight(code: string, language: string | undefined) {
  const instance = await getHighlighter()
  const lang = (language && languageAliases[language.toLowerCase()]) || "text"
  return instance.codeToHtml(code, {
    lang,
    themes: { light: "github-light", dark: "github-dark" },
    defaultColor: "light",
  })
}
