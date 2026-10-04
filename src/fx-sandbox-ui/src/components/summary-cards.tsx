import { Card, CardContent } from "@/components/ui/card"
import { useSandboxState } from "@/hooks/use-sandbox-state"
import { formatSignedUsd, formatUsd } from "@/lib/format"
import { cn } from "@/lib/utils"

function Stat({
  label,
  value,
  className,
}: {
  label: string
  value: string
  className?: string
}) {
  return (
    <Card size="sm">
      <CardContent>
        <div className="text-xs text-muted-foreground">{label}</div>
        <div className={cn("font-mono text-xl font-medium", className)}>
          {value}
        </div>
      </CardContent>
    </Card>
  )
}

export function SummaryCards() {
  const { data, isPending } = useSandboxState()

  if (isPending || !data) {
    return (
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {["Cash", "Equity", "Unrealised P&L", "Pending orders"].map((label) => (
          <Stat key={label} label={label} value="…" />
        ))}
      </div>
    )
  }

  const pending = data.orders.filter((o) => o.status === "Pending").length
  const pnlClass =
    data.unrealisedPnl > 0
      ? "text-green-700 dark:text-green-400"
      : data.unrealisedPnl < 0
        ? "text-red-700 dark:text-red-400"
        : undefined

  return (
    <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
      <Stat label="Cash" value={formatUsd(data.cash)} />
      <Stat label="Equity" value={formatUsd(data.equity)} />
      <Stat
        label="Unrealised P&L"
        value={formatSignedUsd(data.unrealisedPnl)}
        className={pnlClass}
      />
      <Stat label="Pending orders" value={String(pending)} />
    </div>
  )
}
