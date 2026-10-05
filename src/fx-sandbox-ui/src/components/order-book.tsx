import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { toast } from "sonner"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { STATE_KEY, useSandboxState } from "@/hooks/use-sandbox-state"
import { api, type Order, type OrderStatus } from "@/lib/api"
import { formatRate, pairLabel } from "@/lib/format"

type Filter = "All" | "Pending" | "Filled"

const FILTERS: Filter[] = ["All", "Pending", "Filled"]

const STATUS_VARIANT: Record<OrderStatus, "positive" | "warning" | "neutral"> =
  { Pending: "warning", Filled: "positive", Cancelled: "neutral" }

const quantity = new Intl.NumberFormat("en-US")

function CancelOrderButton({ order }: { order: Order }) {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)

  const cancel = useMutation({
    mutationFn: () => api.cancelOrder(order.id),
    onSuccess: () => toast.success(`Order ${order.id} cancelled`),
    onError: (error) =>
      toast.error(
        error instanceof Error ? error.message : "Could not cancel the order"
      ),
    // A 409 means the order filled first, so refresh either way.
    onSettled: () => {
      setOpen(false)
      void queryClient.invalidateQueries({ queryKey: STATE_KEY })
    },
  })

  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      <AlertDialogTrigger
        render={<Button variant="outline" size="xs" />}
        aria-label={`Cancel order ${order.id}`}
      >
        Cancel
      </AlertDialogTrigger>
      <AlertDialogContent size="sm">
        <AlertDialogHeader>
          <AlertDialogTitle>Cancel order {order.id}?</AlertDialogTitle>
          <AlertDialogDescription>
            {order.side} {quantity.format(order.quantity)}{" "}
            {pairLabel(order.pair)} at {formatRate(order.limitPrice)}. This
            can't be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep order</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={cancel.isPending}
            onClick={() => cancel.mutate()}
          >
            Cancel order
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

export function OrderBook() {
  const { data } = useSandboxState()
  const [filter, setFilter] = useState<Filter>("All")

  // Newest first. "All" includes cancelled orders; the other tabs match their status.
  const orders = [...(data?.orders ?? [])]
    .filter((o) => filter === "All" || o.status === filter)
    .sort((a, b) => b.id - a.id)

  return (
    <Card size="sm">
      <CardHeader className="items-center">
        <CardTitle>Order book</CardTitle>
        <CardAction className="self-center">
          <Tabs value={filter} onValueChange={(v) => setFilter(v as Filter)}>
            <TabsList>
              {FILTERS.map((f) => (
                <TabsTrigger key={f} value={f}>
                  {f}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        </CardAction>
      </CardHeader>
      <CardContent>
        <Table className="table-fixed">
          <TableHeader>
            <TableRow>
              <TableHead className="w-[10%]">Id</TableHead>
              <TableHead className="w-[17%]">Pair</TableHead>
              <TableHead className="w-[11%]">Side</TableHead>
              <TableHead className="w-[17%] text-right">Qty (USD)</TableHead>
              <TableHead className="w-[17%] text-right">Limit</TableHead>
              <TableHead className="w-[16%] pl-6">Status</TableHead>
              <TableHead className="w-[12%]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {orders.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={7}
                  className="py-6 text-center text-muted-foreground"
                >
                  No orders
                </TableCell>
              </TableRow>
            ) : (
              orders.map((o) => (
                <TableRow key={o.id}>
                  <TableCell className="font-mono">{o.id}</TableCell>
                  <TableCell>{pairLabel(o.pair)}</TableCell>
                  <TableCell
                    className={
                      o.side === "Buy" ? "text-positive" : "text-negative"
                    }
                  >
                    {o.side}
                  </TableCell>
                  <TableCell className="text-right font-mono">
                    {quantity.format(o.quantity)}
                  </TableCell>
                  <TableCell className="text-right font-mono">
                    {formatRate(o.limitPrice)}
                  </TableCell>
                  <TableCell className="pl-6">
                    <Badge variant={STATUS_VARIANT[o.status]}>{o.status}</Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    {o.status === "Pending" && <CancelOrderButton order={o} />}
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
