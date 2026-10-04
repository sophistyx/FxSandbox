import { Header } from "@/components/header"
import { RateTickers } from "@/components/rate-tickers"
import { SummaryCards } from "@/components/summary-cards"
import { useSandboxLive } from "@/hooks/use-sandbox-live"

export function App() {
  const status = useSandboxLive()

  return (
    <div className="mx-auto flex min-h-svh max-w-5xl flex-col gap-4 p-6">
      <Header status={status} />
      <SummaryCards />
      <RateTickers />
    </div>
  )
}

export default App
