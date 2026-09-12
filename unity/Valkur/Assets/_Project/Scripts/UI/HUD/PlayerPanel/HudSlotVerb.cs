using System;
using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// An action a slot can show that is NOT a spell: open the bag, talk to whoever is in reach,
    /// change posture. Handed to <see cref="HudAbilitySlot.SetVerb"/>, which then draws it with
    /// the same frame, key cap, flash and glow a spell gets — one slot component for every place
    /// the HUD offers an action, which is rule R5 of <c>.github/HUD_VISUAL_LANGUAGE.md</c>.
    ///
    /// <para><b>Three states, all derived every frame:</b> available (full tint), idle (nothing
    /// to act on right now — dimmed, never hidden, because a verb that vanishes and reappears as
    /// the player walks reads as the bar flickering), and active (the panel it opens is open — a
    /// steady ring, since it is a STATE the player reads, not an event).</para>
    /// </summary>
    public sealed class HudSlotVerb
    {
        /// <summary>Stable id, for the tests and for finding a slot by meaning.</summary>
        public string Id;

        /// <summary>The tooltip's title, in Spanish.</summary>
        public string Title;

        /// <summary>The pixel glyph drawn at its native size in the middle of the slot.</summary>
        public Sprite Glyph;

        /// <summary>The glyph's colour, and the colour its flash and ring take.</summary>
        public Color Tint = Color.white;

        /// <summary>How much of <see cref="Tint"/> the glyph keeps while idle.</summary>
        public float IdleStrength = 0.38f;

        /// <summary>Whether there is anything to act on. Null reads as always.</summary>
        public Func<bool> IsAvailable;

        /// <summary>Whether what the verb opens is open. Null reads as never.</summary>
        public Func<bool> IsActive;

        /// <summary>The tooltip's second line. Null reads as nothing.</summary>
        public Func<string> Detail;

        /// <summary>What a click does. Null makes the slot display-only.</summary>
        public Action Invoke;

        /// <summary>
        /// A count the verb wants to announce while its panel is CLOSED — unspent talent points,
        /// unread quests, new items. Null, or a value of 0, draws nothing.
        ///
        /// <para>It exists because a window the player has to open to find out whether it has
        /// anything for them is a window they stop opening. Levelling up granted a skill point and
        /// the game said so nowhere at all: the number lived in a panel reached from this very
        /// slot, and nothing on the slot changed.</para>
        /// </summary>
        public Func<int> Badge;

        /// <summary>The count to draw, or 0 for none.</summary>
        public int BadgeCount => Badge == null ? 0 : Mathf.Max(0, Badge());

        public bool Available => IsAvailable == null || IsAvailable();
        public bool Active => IsActive != null && IsActive();
    }
}
