import { CircleIcon, SlidersHorizontalIcon } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import type { ConnectionStatus } from "@/hooks/use-sandbox-live"

const STATUS: Record<
  ConnectionStatus,
  { label: string; variant: "positive" | "warning" | "negative" | "neutral" }
> = {
  live: { label: "Live", variant: "positive" },
  connecting: { label: "Connecting", variant: "neutral" },
  reconnecting: { label: "Reconnecting", variant: "warning" },
  offline: { label: "Offline", variant: "negative" },
}

export function Header({ status }: { status: ConnectionStatus }) {
  const { label, variant } = STATUS[status]
  return (
    <header className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <SlidersHorizontalIcon className="size-4" aria-hidden />
        <h1 className="text-base font-medium">FX sandbox</h1>
      </div>
      <Badge variant={variant} role="status">
        <CircleIcon className="fill-current" aria-hidden />
        {label}
      </Badge>
    </header>
  )
}
