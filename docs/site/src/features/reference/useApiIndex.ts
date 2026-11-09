import { useMemo } from "react"
import { getRouteApi } from "@tanstack/react-router"
import type { ApiIndex, ApiTypeSummary } from "./reference.api"

const rootRoute = getRouteApi("__root__")

/** The navigation index loaded by the root route; null until DocGen has generated data. */
export function useApiIndex(): ApiIndex | null {
  return rootRoute.useLoaderData()
}

export function useTypesById(index: ApiIndex | null) {
  return useMemo(() => new Map<string, ApiTypeSummary>((index?.types ?? []).map((type) => [type.id, type])), [index])
}

/** Maps each declaration file to the type it is named after (or its first type). */
export function useSourceTypes(index: ApiIndex | null) {
  return useMemo(() => {
    const map = new Map<string, string>()
    for (const type of index?.types ?? []) {
      if (!type.source) continue
      const fileName = type.source.split("/").pop()?.replace(/\.cs$/, "")
      if (!map.has(type.source) || fileName === type.name) map.set(type.source, type.id)
    }
    return map
  }, [index])
}
