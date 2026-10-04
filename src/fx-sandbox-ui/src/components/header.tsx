import { CircleIcon, SlidersHorizontalIcon } from "lucide-react"

import { Badge } from "@/components/ui/badge"

export function Header() {
  return (
    <header className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <SlidersHorizontalIcon className="size-4" aria-hidden />
        <h1 className="text-base font-medium">FX sandbox</h1>
      </div>
      <Badge variant="secondary">
        <CircleIcon className="fill-current" aria-hidden />
        Live
      </Badge>
    </header>
  )
}
