using System;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>Which set of verbs the player's input currently means.</summary>
    public enum Stance
    {
        /// <summary>Fighting. Every combat binding is live — this is the historical behaviour.</summary>
        War = 0,

        /// <summary>
        /// Everyday life: gathering, trading, talking. Combat is not remapped, it is
        /// structurally unavailable.
        /// </summary>
        Peace = 1,
    }

    /// <summary>
    /// The player's stance — the single answer to "does a combat binding do anything right now".
    ///
    /// <para>WHY A SAFE POSTURE RATHER THAN A SECOND KEY LAYOUT. Nothing in the damage path
    /// reads a faction: <c>Projectile</c> and <c>MeleeCombat</c> contain no such check, and
    /// <c>EntitySetup</c> gives every NPC a <c>Health</c>. Combined with left click both
    /// locking a target AND casting the primary spell — deliberate, so clicking an enemy
    /// attacks it in one gesture — the result is that clicking a vendor to talk to her throws
    /// a fireball at her, and double-clicking her opens the conversation with two already in
    /// flight. She can be killed by trying to trade with her. Peace is what makes that
    /// impossible, and the keyboard it frees up is the consequence, not the reason.</para>
    ///
    /// <para>WHY A STATIC IN CORE. The HUD chip lives in <c>Valkur.UI</c> and the gate lives in
    /// <c>Valkur.Gameplay</c>, and <c>Gameplay → UI</c> is forbidden, so Core is the only floor
    /// both can stand on. It is not a component on the player because <c>HUDBootstrap</c> polls
    /// for that player and the chip has to exist and read correctly before it arrives.
    /// <see cref="Valkur.Core.Input.InputBlocker"/> is the precedent for the shape, down to the
    /// Domain-Reload reset hook — with Domain Reload off, a stance left in Peace would survive
    /// into the next Play session and read as combat being broken.</para>
    ///
    /// <para>WHERE IT IS CONSULTED. At the READER, never at the action map.
    /// <c>InputBlocker</c>'s own comment records why: half this project reads through
    /// <c>MouseInputManager</c> / <c>KeyboardInputManager</c>, which OR the legacy backend to
    /// survive the recurring 2022.3 event-drop bug, so <c>Map.Disable</c> silences the bound
    /// actions and leaves every helper-polling callsite untouched. A stance built on enabling
    /// and disabling a map would leak exactly there, and silently.</para>
    ///
    /// <para>It DEFAULTS TO WAR so nothing behaves differently until the player asks: the
    /// feature is purely additive, and a regression hunt never starts by suspecting it.</para>
    /// </summary>
    public static class PlayerStance
    {
        /// <summary>The live stance. Written only by <see cref="Set"/>.</summary>
        public static Stance Current { get; private set; } = Stance.War;

        public static bool IsPeace => Current == Stance.Peace;
        public static bool IsWar   => Current == Stance.War;

        /// <summary>Raised after <see cref="Current"/> changes, never on a no-op set.</summary>
        public static event Action<Stance> OnChanged;

        public static void Set(Stance next)
        {
            if (next == Current) return;
            Current = next;
            OnChanged?.Invoke(next);
        }

        public static void Toggle() => Set(IsPeace ? Stance.War : Stance.Peace);

        /// <summary>
        /// Test hook: drop the stance AND its subscribers. Public for the same reason
        /// <see cref="Valkur.Core.Input.InputService.ResetForTests"/> is — an event cannot be
        /// cleared from outside the class that declares it, and with Domain Reload off a
        /// fixture's lambdas otherwise accumulate across every later fixture in the session.
        /// </summary>
        public static void ResetForTests()
        {
            Current = Stance.War;
            OnChanged = null;
        }

        /// <summary>
        /// Domain Reload is OFF, so both the value and the subscriber list survive into the
        /// next Play session — the second carrying delegates that point at destroyed HUD
        /// objects. Clearing the event matters as much as clearing the stance.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Current = Stance.War;
            OnChanged = null;
        }
    }
}
