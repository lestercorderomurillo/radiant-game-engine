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
    themes: [
      {
        name: "radiant-light", type: "light",
        colors: { "editor.foreground": "#262626", "editor.background": "#f5f5f5" },
        tokenColors: [
          { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#595959", fontStyle: "italic" } },
          { scope: ["keyword", "storage"], settings: { foreground: "#171717", fontStyle: "bold" } },
          { scope: ["string", "constant.numeric"], settings: { foreground: "#525252" } },
        ],
      },
      {
        name: "radiant-dark", type: "dark",
        colors: { "editor.foreground": "#e5e5e5", "editor.background": "#171717" },
        tokenColors: [
          { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#a3a3a3", fontStyle: "italic" } },
          { scope: ["keyword", "storage"], settings: { foreground: "#fafafa", fontStyle: "bold" } },
          { scope: ["string", "constant.numeric"], settings: { foreground: "#bfbfbf" } },
        ],
      },
    ],
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
    themes: { light: "radiant-light", dark: "radiant-dark" },
    defaultColor: "light",
  })
}
