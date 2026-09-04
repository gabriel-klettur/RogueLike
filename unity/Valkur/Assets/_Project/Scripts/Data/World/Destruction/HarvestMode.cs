namespace Valkur.Data
{
    /// <summary>
    /// What working a building with the interact key actually DOES to it.
    ///
    /// <para>The two are not the same statement and forcing one onto the other is the
    /// "internally consistent, wrong on screen" failure this project keeps recording. A
    /// tree is CONSUMED by chopping: the thing you were working on is gone and a stump is
    /// left. A mine is not — you cannot destroy a mountain with a pick, you exhaust the
    /// seam it exposes and come back when it has refilled. Expressing a mine as a building
    /// whose durability reached zero would work perfectly in code and read, on screen, as
    /// the player deleting a hillside.</para>
    /// </summary>
    public enum HarvestMode
    {
        /// <summary>
        /// Work reduces durability. At zero the building is destroyed: it drops its table,
        /// swaps to its remains and (optionally) stops blocking. Trees, crates, barrels.
        /// This is the mode that also accepts ordinary combat blows, because it is the mode
        /// whose obstacle can meaningfully be broken.
        /// </summary>
        Destroy = 0,

        /// <summary>
        /// Work consumes CHARGES. The building survives at zero, marked spent, and refills
        /// after <see cref="DestructionProfile.regrowSeconds"/>. Mines, ore seams, crystal
        /// clusters, wells. A node in this mode is deliberately NOT a destructible obstacle,
        /// so no sword swing or stray fireball can deplete it by accident.
        /// </summary>
        Deplete = 1,
    }
}
