import { useEffect, useState } from "react"
import { HubConnectionBuilder } from "@microsoft/signalr"
import { useQueryClient } from "@tanstack/react-query"

import { toast } from "sonner"

import { STATE_KEY } from "@/hooks/use-sandbox-state"
import type { Order, SandboxState } from "@/lib/api"
import { formatRate, pairLabel } from "@/lib/format"

const RETRY_MS = 5000

export type ConnectionStatus =
  "connecting" | "live" | "reconnecting" | "offline"

/**
 * Connects to the SignalR hub for the lifetime of the calling component, pushing each
 * `snapshot` into the `['state']` query cache and refetching after a reconnect.
 */
export function useSandboxLive(): ConnectionStatus {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<ConnectionStatus>("connecting")

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/sandbox")
      .withAutomaticReconnect()
      .build()

    connection.on("snapshot", (snapshot: SandboxState) => {
      queryClient.setQueryData(STATE_KEY, snapshot)
    })
    connection.on("orderFilled", (order: Order) => {
      toast.success(
        `Order ${order.id} filled at ${formatRate(order.limitPrice)}`,
        { description: `${order.side} ${pairLabel(order.pair)}` }
      )
    })
    connection.onreconnecting(() => setStatus("reconnecting"))
    connection.onreconnected(() => {
      setStatus("live")
      void queryClient.invalidateQueries({ queryKey: STATE_KEY })
    })
    // Automatic reconnect gives up after its retries, so start again from the close handler.
    let disposed = false
    let retry: ReturnType<typeof setTimeout> | undefined

    const start = () => {
      connection
        .start()
        .then(() => {
          if (disposed) return
          setStatus("live")
          // Anything missed while the connection was down or opening.
          void queryClient.invalidateQueries({ queryKey: STATE_KEY })
        })
        .catch(() => {
          if (disposed) return
          setStatus("offline")
          retry = setTimeout(start, RETRY_MS)
        })
    }

    connection.onclose(() => {
      if (disposed) return
      setStatus("offline")
      retry = setTimeout(start, RETRY_MS)
    })

    // Defer the first start by a tick. StrictMode mounts, unmounts and remounts synchronously, so
    // the throwaway first effect is cleaned up before it opens a connection; stopping a connection
    // mid-negotiation is what makes SignalR log "stopped during negotiation" as an error.
    retry = setTimeout(start, 0)

    return () => {
      disposed = true
      clearTimeout(retry)
      void connection.stop()
    }
  }, [queryClient])

  return status
}
