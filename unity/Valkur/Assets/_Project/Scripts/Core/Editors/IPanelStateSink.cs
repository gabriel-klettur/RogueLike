namespace Valkur.Core.Editors
{
    /// <summary>
    /// Where a floating panel's remembered open/closed bit actually lives.
    ///
    /// <c>DraggablePanel</c> used to own that bit outright, in PlayerPrefs. It now asks a
    /// sink, so there is exactly ONE owner of panel visibility with two interchangeable
    /// backends: the default PlayerPrefs one (unchanged behaviour for any panel the
    /// workspace layer does not manage) and the workspace service (which folds the bit into
    /// the editor's document alongside geometry and selection).
    ///
    /// The alternative — leaving PlayerPrefs in place and adding the workspace beside it —
    /// is the two-owners bug this project keeps re-learning: nine systems each caching
    /// <c>SpriteRenderer.color</c> as "the original", two constants holding one spin-up
    /// duration, two writers disagreeing on an FSM animation-map key. Each was correct
    /// alone and wrong together.
    /// </summary>
    public interface IPanelStateSink
    {
        /// <summary>Was this panel left closed? Unknown keys answer false — panels default to open.</summary>
        bool IsClosed(string key);

        void SetClosed(string key, bool closed);

        void Forget(string key);
    }
}
