export type Pair = "UsdEur" | "UsdGbp" | "UsdChf"
export type Side = "Buy" | "Sell"
export type OrderStatus = "Pending" | "Filled" | "Cancelled"

export interface Order {
  id: number
  pair: Pair
  side: Side
  quantity: number
  limitPrice: number
  status: OrderStatus
}

export interface Rate {
  pair: Pair
  rate: number
  history: number[]
}

export interface Position {
  pair: Pair
  direction: "Long" | "Short"
  /** Signed USD notional: positive is long, negative is short. */
  quantity: number
  entryPrice: number
  rate: number | null
  unrealisedPnl: number
}

export interface SandboxState {
  cash: number
  equity: number
  unrealisedPnl: number
  rates: Rate[]
  positions: Position[]
  orders: Order[]
}

export interface PlaceOrderRequest {
  pair: Pair
  side: Side
  quantity: number
  limitPrice: number
}

export class ApiError extends Error {
  readonly status: number
  readonly errors?: Record<string, string[]>

  constructor(
    status: number,
    message: string,
    errors?: Record<string, string[]>
  ) {
    super(message)
    this.name = "ApiError"
    this.status = status
    this.errors = errors
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
  })
  if (!response.ok) {
    // Errors arrive as ProblemDetails (RFC 9457), with `errors` on validation failures.
    const problem = await response.json().catch(() => null)
    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? response.statusText,
      problem?.errors
    )
  }
  return (await response.json()) as T
}

export const api = {
  getState: () => request<SandboxState>("/api/state"),
  placeOrder: (order: PlaceOrderRequest) =>
    request<Order>("/api/orders", {
      method: "POST",
      body: JSON.stringify(order),
    }),
  cancelOrder: (id: number) =>
    request<Order>(`/api/orders/${id}`, { method: "DELETE" }),
  reset: () => request<SandboxState>("/api/reset", { method: "POST" }),
}
