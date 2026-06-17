// -----------------------------------------------------------------------------
// PriceLevel.cs
//
// Defines `PriceLevel` — one aggregated (price, volume) row of a depth snapshot.
//
// This is the atom that both the price ladder and the published snapshots are
// built from. It is a small immutable value type, so comparing levels — which is
// how the engine detects a changed snapshot — is structural and allocation-free.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// A single aggregated level of a price-depth snapshot: the total resting
/// volume across every order at one price, on one side of the book.
/// </summary>
/// <param name="Price">
/// The raw integer price exactly as it appears on the wire (a signed, fixed
/// four-decimal-place value — e.g. <c>318800</c> represents 31.88 dollars).
/// The output format prints this raw integer, so no scaling is applied.
/// </param>
/// <param name="Volume">The sum of the volume of every order resting at <paramref name="Price"/>.</param>
public readonly record struct PriceLevel(int Price, long Volume);
