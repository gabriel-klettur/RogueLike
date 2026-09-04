namespace Valkur.Data
{
    /// <summary>
    /// HOW a blow was delivered, against a <see cref="MaterialClass"/>.
    ///
    /// <para>Physical tools and magical elements share one axis on purpose. A resistance
    /// matrix asks a single question — "does this get through?" — and answering it with
    /// two parallel tables (tool-vs-material and element-vs-material) means every new
    /// material is authored twice and the two halves drift. Fire against wood and an axe
    /// against wood are the same kind of statement.</para>
    ///
    /// <para>The <see cref="SpellElement"/> values map onto the magical half; the physical
    /// half is resolved from the attacker's equipped tool. <see cref="None"/> is bare
    /// hands, and is what an unarmed player gets — it is a real row in the table, not a
    /// missing one.</para>
    /// </summary>
    public enum DamageClass
    {
        None = 0,

        // ── Physical ──────────────────────────────────────────────────────────────
        Axe = 1,
        Pick = 2,
        Blade = 3,
        Blunt = 4,

        // ── Magical (mirrors SpellElement) ────────────────────────────────────────
        Fire = 5,
        Ice = 6,
        Lightning = 7,
        Arcane = 8,
        Dark = 9,
        Light = 10,
    }
}
