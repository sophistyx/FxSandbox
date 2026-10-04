import { CircleIcon, SlidersHorizontalIcon } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import type { ConnectionStatus } from "@/hooks/use-sandbox-live"
import { cn } from "@/lib/utils"

const STATUS: Record<ConnectionStatus, { label: string; className?: string }> =
  {
    live: {
      label: "Live",
      className: "text-green-700 dark:text-green-400",
    },
    connecting: { label: "Connecting" },
    reconnecting: {
      label: "Reconnecting",
      className: "text-amber-700 dark:text-amber-400",
    },
    offline: { label: "Offline", className: "text-red-700 dark:text-red-400" },
  }

export function Header({ status }: { status: ConnectionStatus }) {
  const { label, className } = STATUS[status]
  return (
    <header className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <SlidersHorizontalIcon className="size-4" aria-hidden />
        <h1 className="text-base font-medium">FX sandbox</h1>
      </div>
      <Badge variant="secondary" className={cn(className)} role="status">
        <CircleIcon className="fill-current" aria-hidden />
        {label}
      </Badge>
    </header>
  )
}
