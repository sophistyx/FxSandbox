import { Header } from "@/components/header"
import { SummaryCards } from "@/components/summary-cards"

export function App() {
  return (
    <div className="mx-auto flex min-h-svh max-w-5xl flex-col gap-4 p-6">
      <Header />
      <SummaryCards />
    </div>
  )
}

export default App
