using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// How big the chat panel is, and how it stays that way between sessions.
    ///
    /// <para>Split from the builder because it answers a different question. The builder says
    /// what the panel is MADE of; this says what SIZE it opens at, which depends on something
    /// the builder cannot see — what the player did last time, and how big their window is
    /// today. Those two can disagree, and reconciling them is the whole of this file.</para>
    /// </summary>
    public partial class ChatUI
    {
        /// <summary>
        /// The size the panel should open at: what the player left it at, or the default,
        /// clamped into what today's viewport can actually hold.
        ///
        /// <para>The clamp is not belt-and-braces. A remembered size is a number from a
        /// PREVIOUS session, and nothing says that session ran at this resolution — a panel
        /// sized on a 2560-wide monitor and restored into a 1366 window would reach past the
        /// right edge, taking its close button with it. The one control that gets a window
        /// unstuck must never be the part that lands off screen.</para>
        /// </summary>
        private static Vector2 ResolveStartingPanelSize()
        {
            // A size saved against a DIFFERENT arrangement of the rows is not a preference,
            // it is a measurement of a panel that no longer exists. Discarded once, on the
            // first open after the layout changed; from then on the player's own drag wins
            // again exactly as before.
            if (PlayerPrefs.GetInt(PREF_LAYOUT_VERSION, 1) != PANEL_LAYOUT_VERSION)
            {
                PlayerPrefs.SetInt(PREF_LAYOUT_VERSION, PANEL_LAYOUT_VERSION);
                PlayerPrefs.Save();
                return ClampPanelSize(new Vector2(PANEL_DEFAULT_W, PANEL_DEFAULT_H));
            }

            var size = new Vector2(
                PlayerPrefs.GetFloat(PREF_PANEL_WIDTH, PANEL_DEFAULT_W),
                PlayerPrefs.GetFloat(PREF_PANEL_HEIGHT, PANEL_DEFAULT_H));

            return ClampPanelSize(size);
        }

        /// <summary>
        /// <paramref name="size"/> held between the authored minimum and what the viewport
        /// allows. Shared by the restore path and the live grip so the two can never disagree
        /// about what is legal.
        /// </summary>
        private static Vector2 ClampPanelSize(Vector2 size)
        {
            Vector2 max = MaxPanelSize();
            return new Vector2(
                Mathf.Clamp(size.x, PANEL_MIN_W, max.x),
                Mathf.Clamp(size.y, PANEL_MIN_H, max.y));
        }

        /// <summary>
        /// The largest the panel may grow, derived from the live viewport rather than
        /// authored.
        ///
        /// <para>A constant ceiling is wrong in both directions at once: it is unreachable on
        /// a small window (the panel is clamped long after it has already run off the screen)
        /// and needlessly small on a large one. <c>Screen</c> is read fresh on every call for
        /// the same reason — the player can resize the game window, and Valkur's own Options
        /// screen changes the resolution at runtime.</para>
        ///
        /// <para>Floored at the minimum so a viewport smaller than <see cref="PANEL_MIN_W"/>
        /// cannot produce a max below the min, which would make <c>Mathf.Clamp</c> return the
        /// max and silently shrink the panel below its own floor.</para>
        /// </summary>
        private static Vector2 MaxPanelSize()
        {
            return new Vector2(
                Mathf.Max(PANEL_MIN_W, Screen.width - PANEL_SCREEN_MARGIN),
                Mathf.Max(PANEL_MIN_H, Screen.height - PANEL_SCREEN_MARGIN));
        }

        /// <summary>
        /// Remembers the size the player just dragged the panel to.
        ///
        /// <para>Called on the END of a drag, not per frame: this writes to disk, and the grip
        /// moves sixty times a second. <c>PlayerPrefs.Save</c> is explicit because Unity only
        /// flushes on a clean quit otherwise, and a session that ends in a crash — or in the
        /// Editor, in a Stop — is exactly the one whose layout the player would notice
        /// losing.</para>
        /// </summary>
        private static void PersistPanelSize(Vector2 size)
        {
            PlayerPrefs.SetFloat(PREF_PANEL_WIDTH, size.x);
            PlayerPrefs.SetFloat(PREF_PANEL_HEIGHT, size.y);
            // Stamped with the drag, so a size and the layout it was measured on are always
            // written together. Writing it only in the restore path would leave a player who
            // never reopens the panel carrying a version that describes someone else's size.
            PlayerPrefs.SetInt(PREF_LAYOUT_VERSION, PANEL_LAYOUT_VERSION);
            PlayerPrefs.Save();
        }
    }
}
