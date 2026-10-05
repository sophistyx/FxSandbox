import { Header } from "@/components/header"
import { OrderForm } from "@/components/order-form"
import { OrderBook } from "@/components/order-book"
import { PositionsTable } from "@/components/positions-table"
import { RateTickers } from "@/components/rate-tickers"
import { SummaryCards } from "@/components/summary-cards"
import { Toaster } from "@/components/ui/sonner"
import { useSandboxLive } from "@/hooks/use-sandbox-live"

export function App() {
  const status = useSandboxLive()

  return (
    <div className="mx-auto flex min-h-svh max-w-5xl flex-col gap-4 p-6">
      <Header status={status} />
      <SummaryCards />
      <RateTickers />
      <div className="grid gap-4 md:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <OrderForm />
        <PositionsTable />
      </div>
      <OrderBook />
      <Toaster />
    </div>
  )
}

export default App
