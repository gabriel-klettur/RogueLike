namespace Valkur.Core.UI
{
    /// <summary>
    /// Which of the two rows a bar is.
    ///
    /// <para>It exists so a row can ask for ITS art rather than for art of a given height. Keying
    /// on the height alone reads correctly right up to the day a style gives both rows the same
    /// number of texels, at which point the resource row silently starts drawing the health row's
    /// painted art — a defect with no symptom in code and an obvious one on screen.</para>
    ///
    /// <para>In its own file rather than beside <see cref="WorldBarSheetLayout"/>: this project
    /// has already been bitten by an enum that lives in another type's file and reads as nested
    /// when it is not (<c>SpellCastAnchor</c>).</para>
    /// </summary>
    public enum WorldBarRow
    {
        /// <summary>The thick row nearest the head. Carries the quarter marks.</summary>
        Health = 0,

        /// <summary>The thin row above it, shared by mana and the dash pip.</summary>
        Resource = 1,
    }
}
