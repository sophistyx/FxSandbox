import { useQuery } from "@tanstack/react-query"

import { api } from "@/lib/api"

export const STATE_KEY = ["state"] as const

export function useSandboxState() {
  return useQuery({ queryKey: STATE_KEY, queryFn: api.getState })
}
