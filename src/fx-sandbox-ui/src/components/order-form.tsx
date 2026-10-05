import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useEffect, useRef } from "react"
import { Controller, useForm, useWatch } from "react-hook-form"
import { toast } from "sonner"
import { z } from "zod"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { STATE_KEY, useSandboxState } from "@/hooks/use-sandbox-state"
import {
  ApiError,
  type Pair,
  type PlaceOrderRequest,
  type Side,
} from "@/lib/api"
import { api } from "@/lib/api"
import { formatRate, pairLabel } from "@/lib/format"

const PAIRS = ["UsdEur", "UsdGbp", "UsdChf"] as const satisfies readonly Pair[]
const PAIR_ITEMS = PAIRS.map((pair) => ({
  value: pair,
  label: pairLabel(pair),
}))

const positiveNumber = (label: string) =>
  z
    .string()
    .trim()
    .min(1, `${label} is required`)
    .refine((v) => Number.isFinite(Number(v)) && Number(v) > 0, {
      message: `${label} must be greater than zero`,
    })

const schema = z.object({
  pair: z.enum(PAIRS),
  side: z.enum(["Buy", "Sell"]),
  quantity: positiveNumber("Quantity"),
  limitPrice: positiveNumber("Limit price"),
})

type FormValues = z.infer<typeof schema>
type FieldName = keyof FormValues

const DEFAULT_QUANTITY = "1000"

const FIELD_NAMES: FieldName[] = ["pair", "side", "quantity", "limitPrice"]

// ProblemDetails keys follow the C# property names (e.g. "LimitPrice"), so match case-insensitively.
const toFieldName = (key: string) =>
  FIELD_NAMES.find((name) => name.toLowerCase() === key.toLowerCase())

export function OrderForm() {
  const queryClient = useQueryClient()
  const { data } = useSandboxState()

  const {
    control,
    register,
    handleSubmit,
    setError,
    resetField,
    formState: { errors, dirtyFields },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      pair: "UsdEur",
      side: "Buy",
      quantity: DEFAULT_QUANTITY,
      limitPrice: "",
    },
  })

  const [pair, side, limitPrice] = useWatch({
    control,
    name: ["pair", "side", "limitPrice"],
  })
  const currentRate = data?.rates.find((r) => r.pair === pair)?.rate

  // The limit is seeded with the market rate once per pair (not on every tick), and only
  // while the user hasn't typed their own value. "Use market" re-seeds it explicitly.
  // resetField makes the seeded value the field's baseline, so `dirty` means "user-edited".
  const seededPair = useRef<Pair | null>(null)
  const limitEdited = !!dirtyFields.limitPrice
  useEffect(() => {
    if (currentRate === undefined || seededPair.current === pair) return
    seededPair.current = pair
    if (!limitEdited)
      resetField("limitPrice", { defaultValue: formatRate(currentRate) })
  }, [pair, currentRate, limitEdited, resetField])

  const applyMarketRate = () => {
    if (currentRate === undefined) return
    resetField("limitPrice", { defaultValue: formatRate(currentRate) })
  }

  const placeOrder = useMutation({
    mutationFn: (order: PlaceOrderRequest) => api.placeOrder(order),
    onSuccess: (order) => {
      void queryClient.invalidateQueries({ queryKey: STATE_KEY })
      toast.success(`Order ${order.id} placed`)
    },
    onError: (error) => {
      const message =
        error instanceof Error ? error.message : "Could not place the order"
      toast.error(message)
      if (error instanceof ApiError && error.errors) {
        for (const [key, messages] of Object.entries(error.errors)) {
          const name = toFieldName(key)
          if (name) setError(name, { message: messages[0] })
        }
      }
    },
  })

  const onSubmit = (values: FormValues) =>
    placeOrder.mutate({
      pair: values.pair,
      side: values.side,
      quantity: Number(values.quantity),
      limitPrice: Number(values.limitPrice),
    })

  const limit = Number(limitPrice)
  const helper =
    limitPrice.trim() && Number.isFinite(limit) && limit > 0
      ? side === "Buy"
        ? `Fills when rate falls to ${limitPrice.trim()} or lower`
        : `Fills when rate rises to ${limitPrice.trim()} or higher`
      : null

  return (
    <Card size="sm">
      <CardHeader>
        <CardTitle>New order</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} noValidate>
          <FieldGroup>
            <div className="grid gap-4">
              <Field>
                <FieldLabel id="pair-label">Pair</FieldLabel>
                <Controller
                  control={control}
                  name="pair"
                  render={({ field }) => (
                    <Select
                      items={PAIR_ITEMS}
                      value={field.value}
                      onValueChange={(value) => field.onChange(value as Pair)}
                    >
                      <SelectTrigger
                        className="w-full"
                        aria-labelledby="pair-label"
                      >
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {PAIR_ITEMS.map((item) => (
                          <SelectItem key={item.value} value={item.value}>
                            {item.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
              </Field>

              <Field>
                <FieldLabel id="side-label">Side</FieldLabel>
                <Controller
                  control={control}
                  name="side"
                  render={({ field }) => (
                    <ToggleGroup
                      variant="outline"
                      className="w-full"
                      aria-labelledby="side-label"
                      value={[field.value]}
                      onValueChange={(value) => {
                        // Keep exactly one side selected: ignore a click on the active toggle.
                        const next = value[0] as Side | undefined
                        if (next) field.onChange(next)
                      }}
                    >
                      <ToggleGroupItem
                        value="Buy"
                        className="flex-1 aria-pressed:bg-positive-soft aria-pressed:text-positive"
                      >
                        Buy
                      </ToggleGroupItem>
                      <ToggleGroupItem
                        value="Sell"
                        className="flex-1 aria-pressed:bg-negative-soft aria-pressed:text-negative"
                      >
                        Sell
                      </ToggleGroupItem>
                    </ToggleGroup>
                  )}
                />
              </Field>

              <Field data-invalid={!!errors.quantity}>
                <FieldLabel htmlFor="quantity">Quantity (USD)</FieldLabel>
                <Input
                  id="quantity"
                  inputMode="decimal"
                  autoComplete="off"
                  aria-invalid={!!errors.quantity}
                  {...register("quantity")}
                />
                <FieldError errors={[errors.quantity]} />
              </Field>

              <Field data-invalid={!!errors.limitPrice}>
                <div className="flex items-center justify-between">
                  <FieldLabel htmlFor="limitPrice">Limit price</FieldLabel>
                  <Button
                    type="button"
                    variant="ghost"
                    size="xs"
                    onClick={applyMarketRate}
                    disabled={currentRate === undefined}
                  >
                    Use market
                  </Button>
                </div>
                <Input
                  id="limitPrice"
                  inputMode="decimal"
                  autoComplete="off"
                  aria-invalid={!!errors.limitPrice}
                  {...register("limitPrice")}
                />
                <FieldError errors={[errors.limitPrice]} />
              </Field>
            </div>

            <div className="flex flex-col gap-3">
              {helper && (
                <FieldDescription className="font-mono">
                  {helper}
                </FieldDescription>
              )}
              <Button
                type="submit"
                className="w-full"
                disabled={placeOrder.isPending}
              >
                {placeOrder.isPending
                  ? "Placing…"
                  : `Place ${side.toLowerCase()} order`}
              </Button>
            </div>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  )
}
