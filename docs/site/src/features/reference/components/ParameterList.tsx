import { Markdown } from "@/common/components/Markdown"
import { t } from "@/common/i18n/t"
import type { ApiNamedDoc, ApiParameter } from "../reference.api"
import { Tokens } from "./Signature"

interface ParameterListProps {
  parameters: (ApiParameter | ApiNamedDoc)[]
  title?: string
}

/** Parameter or type-parameter definitions with types, defaults and descriptions. */
export function ParameterList({ parameters, title = t("reference.parameters") }: ParameterListProps) {
  return (
    <div className="space-y-2">
      <h4 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h4>
      <dl className="divide-y rounded-md border">
        {parameters.map((parameter) => {
          const typed = "type" in parameter ? parameter : undefined
          return (
            <div key={parameter.name} className="grid gap-1 px-3 py-2 text-sm sm:grid-cols-[minmax(8rem,auto)_1fr] sm:gap-4">
              <dt className="flex flex-wrap items-baseline gap-x-2 font-mono">
                <span className="font-medium">{parameter.name}</span>
                {typed?.modifier && <span className="sx-k text-xs">{typed.modifier}</span>}
                {typed?.type && <Tokens tokens={typed.type} className="text-xs" />}
              </dt>
              <dd className="min-w-0 text-muted-foreground">
                {parameter.description && <Markdown source={parameter.description} variant="compact" />}
                {typed?.defaultValue && <p className="mt-0.5 text-xs">{t("reference.default", { value: typed.defaultValue })}</p>}
              </dd>
            </div>
          )
        })}
      </dl>
    </div>
  )
}
