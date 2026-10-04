import { Header } from "@/components/header"
import { OrderForm } from "@/components/order-form"
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
      <OrderForm />
      <Toaster />
    </div>
  )
}

export default App
