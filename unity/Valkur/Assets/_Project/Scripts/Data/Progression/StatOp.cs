namespace Valkur.Data
{
    /// <summary>
    /// How one <see cref="StatModifier"/> combines with the others on the same stat.
    ///
    /// The whole point of having three operations rather than one is that they compose
    /// in a fixed, published order, so a designer can reason about a build without
    /// knowing the order the modifiers happened to be added in:
    ///
    /// <code>
    /// final = (base + Σ Flat) × (1 + Σ PercentAdd) × Π (1 + PercentMult)
    /// </code>
    ///
    /// <see cref="PercentAdd"/> is the workhorse — ten nodes of "+5% damage" give +50 %,
    /// which is what a player expects from a talent row. <see cref="PercentMult"/> is
    /// reserved for the rare source that should stay strong in a stacked build (a
    /// capstone, an elite affix); five of those give ×1.28, not ×1.25, and that gap is
    /// the reason both exist. Mixing them into one bucket is the classic ARPG balance
    /// bug where late-game additive stacking makes every other source worthless.
    /// </summary>
    public enum StatOp
    {
        /// <summary>Added to the base before any percentage applies.</summary>
        Flat = 0,

        /// <summary>Summed with every other PercentAdd, then applied once. 0.05 = +5 %.</summary>
        PercentAdd = 1,

        /// <summary>Applied as its own independent factor. 0.05 = ×1.05.</summary>
        PercentMult = 2,
    }
}
