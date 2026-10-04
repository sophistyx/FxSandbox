namespace FxSandbox.Core;

/// <summary>A spot rate for a pair: quote-currency units per 1 USD.</summary>
public readonly record struct Rate(Pair Pair, decimal Value);
