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
        name: "radiant-prism", type: "dark",
        colors: { "editor.foreground": "#e8dcdf", "editor.background": "#0a0607" },
        tokenColors: [
          { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#6e5a60", fontStyle: "italic" } },
          { scope: ["keyword", "storage", "keyword.operator.new"], settings: { foreground: "#ff5577" } },
          { scope: ["entity.name.type", "entity.name.class", "storage.type", "support.type", "entity.other.inherited-class"], settings: { foreground: "#ff9466" } },
          { scope: ["entity.name.function", "support.function"], settings: { foreground: "#ffc27a" } },
          { scope: ["string", "punctuation.definition.string"], settings: { foreground: "#ffb0a0" } },
          { scope: ["constant.numeric", "constant.language"], settings: { foreground: "#ff7aa8" } },
          { scope: ["variable.other.object.property", "variable.other.property", "entity.name.variable.property"], settings: { foreground: "#f0c8d2" } },
          { scope: ["punctuation", "keyword.operator", "meta.brace"], settings: { foreground: "#7a5f67" } },
          { scope: ["entity.name.namespace", "entity.name.type.namespace"], settings: { foreground: "#c9a9b2" } },
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

/** Highlights code with the Prism theme (the same dark editor in both site themes); unknown languages render as plain escaped text. */
export async function highlight(code: string, language: string | undefined) {
  const instance = await getHighlighter()
  const lang = (language && languageAliases[language.toLowerCase()]) || "text"
  return instance.codeToHtml(code, {
    lang,
    themes: { light: "radiant-prism", dark: "radiant-prism" },
    defaultColor: "light",
  })
}
