import { Card, CardContent } from "@/components/ui/card"
import { useSandboxState } from "@/hooks/use-sandbox-state"
import type { Rate } from "@/lib/api"
import { formatPercent, formatRate, pairLabel } from "@/lib/format"
import { cn } from "@/lib/utils"

const UP = "text-positive"
const DOWN = "text-negative"

function Sparkline({
  values,
  className,
}: {
  values: number[]
  className?: string
}) {
  if (values.length < 2) return null
  const min = Math.min(...values)
  const range = Math.max(...values) - min || 1
  const width = 100
  const height = 28
  const points = values
    .map((v, i) => {
      const x = (i / (values.length - 1)) * width
      const y = height - 2 - ((v - min) / range) * (height - 4)
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(" ")

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className={cn("h-7 w-full", className)}
      aria-hidden
    >
      <polyline
        points={points}
        fill="none"
        stroke="currentColor"
        strokeWidth={1.5}
        strokeLinejoin="round"
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  )
}

function Ticker({ rate }: { rate: Rate }) {
  const open = rate.history[0] ?? rate.rate
  const change = open === 0 ? 0 : (rate.rate - open) / open
  const tone = change > 0 ? UP : change < 0 ? DOWN : "text-muted-foreground"

  return (
    <Card size="sm">
      <CardContent>
        <div className="flex items-baseline justify-between">
          <span className="text-xs text-muted-foreground">
            {pairLabel(rate.pair)}
          </span>
          <span className={cn("font-mono text-xs", tone)}>
            {formatPercent(change)}
          </span>
        </div>
        <div className="font-mono text-xl font-medium">
          {formatRate(rate.rate)}
        </div>
        <Sparkline values={rate.history} className={tone} />
      </CardContent>
    </Card>
  )
}

export function RateTickers() {
  const { data } = useSandboxState()

  return (
    <div className="grid gap-3 sm:grid-cols-3">
      {data
        ? data.rates.map((rate) => <Ticker key={rate.pair} rate={rate} />)
        : ["UsdEur", "UsdGbp", "UsdChf"].map((pair) => (
            <Card key={pair} size="sm">
              <CardContent className="h-[88px]" />
            </Card>
          ))}
    </div>
  )
}
