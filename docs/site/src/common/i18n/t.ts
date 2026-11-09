import { en, type MessageKey } from "./en"

export type { MessageKey }

/** Looks up UI copy by key and substitutes {name} placeholders. */
export function t(key: MessageKey, values?: Record<string, string | number>) {
  const message: string = en[key]
  if (!values) return message
  return message.replace(/\{(\w+)\}/g, (match, name: string) => (name in values ? String(values[name]) : match))
}
