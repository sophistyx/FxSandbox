import { useEffect, useState } from "react"
import { HubConnectionBuilder } from "@microsoft/signalr"
import { useQueryClient } from "@tanstack/react-query"

import { STATE_KEY } from "@/hooks/use-sandbox-state"
import type { SandboxState } from "@/lib/api"

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

    start()

    return () => {
      disposed = true
      clearTimeout(retry)
      void connection.stop()
    }
  }, [queryClient])

  return status
}
