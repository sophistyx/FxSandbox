import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { useSandboxState } from "@/hooks/use-sandbox-state"
import { formatRate, formatSignedUsd, pairLabel } from "@/lib/format"
import { cn } from "@/lib/utils"

const UP = "text-positive"
const DOWN = "text-negative"

const quantity = new Intl.NumberFormat("en-US", { signDisplay: "negative" })

export function PositionsTable() {
  const { data } = useSandboxState()
  const positions = data?.positions ?? []

  return (
    <Card size="sm">
      <CardHeader>
        <CardTitle>Open positions</CardTitle>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Pair</TableHead>
              <TableHead className="text-right">Qty (USD)</TableHead>
              <TableHead className="text-right">Entry</TableHead>
              <TableHead className="text-right">Rate</TableHead>
              <TableHead className="text-right">P&amp;L</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {positions.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={5}
                  className="py-6 text-center text-muted-foreground"
                >
                  No open positions
                </TableCell>
              </TableRow>
            ) : (
              positions.map((p) => (
                <TableRow key={p.pair}>
                  <TableCell>
                    <div className="flex items-center gap-2">
                      {pairLabel(p.pair)}
                      <Badge
                        variant={
                          p.direction === "Long" ? "positive" : "negative"
                        }
                      >
                        {p.direction}
                      </Badge>
                    </div>
                  </TableCell>
                  <TableCell className="text-right font-mono">
                    {quantity.format(p.quantity)}
                  </TableCell>
                  <TableCell className="text-right font-mono">
                    {formatRate(p.entryPrice)}
                  </TableCell>
                  <TableCell className="text-right font-mono">
                    {p.rate === null ? "–" : formatRate(p.rate)}
                  </TableCell>
                  <TableCell
                    className={cn(
                      "text-right font-mono",
                      p.unrealisedPnl > 0 && UP,
                      p.unrealisedPnl < 0 && DOWN
                    )}
                  >
                    {formatSignedUsd(p.unrealisedPnl)}
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}
