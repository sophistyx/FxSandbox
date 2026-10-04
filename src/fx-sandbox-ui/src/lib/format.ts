import type { Pair } from "@/lib/api"

const PAIR_LABELS: Record<Pair, string> = {
  UsdEur: "USD/EUR",
  UsdGbp: "USD/GBP",
  UsdChf: "USD/CHF",
}

export const pairLabel = (pair: Pair) => PAIR_LABELS[pair]

const usd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
})

const signedUsd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  signDisplay: "exceptZero",
})

const percent = new Intl.NumberFormat("en-US", {
  style: "percent",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: "exceptZero",
})

export const formatRate = (value: number) => value.toFixed(5)
export const formatPercent = (fraction: number) => percent.format(fraction)
export const formatUsd = (value: number) => usd.format(value)
export const formatSignedUsd = (value: number) => signedUsd.format(value)
