using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One switchable piece of the cast flourish: which knobs it owns, and the single value
    /// that stops it being drawn.
    /// </summary>
    internal readonly struct CastFlourishPiece
    {
        /// <summary>Header text, and the verb an undo step uses ("Turn off Sigil").</summary>
        public readonly string Section;

        /// <summary>Every <c>CastFlourishProfile</c> knob under this header, in row order.</summary>
        public readonly string[] Knobs;

        /// <summary>
        /// The one knob whose value decides whether the piece is drawn at all, or null for a
        /// LOCKED section — one that cannot honestly be switched off.
        /// </summary>
        public readonly string GateKnob;

        /// <summary>The gate's "not drawn" value. Must be of the gate knob's exact type.</summary>
        public readonly object OffValue;

        /// <summary>
        /// Where a switched-ON piece takes its values from when the spell's own family draws
        /// none. A family FUNCTION rather than a table of constants, so a seeded ON sizes
        /// itself off the spell's own data instead of inventing magic numbers — and so this
        /// table can never become a fourth set of tuning values to drift.
        /// </summary>
        public readonly Func<SpellDefinition, CastFlourishProfile> Donor;

        /// <summary>A sibling section this one can silently depend on, or null.</summary>
        public readonly string DependsOn;

        public CastFlourishPiece(string section, string gateKnob, object offValue,
            Func<SpellDefinition, CastFlourishProfile> donor, string[] knobs, string dependsOn = null)
        {
            Section = section; GateKnob = gateKnob; OffValue = offValue;
            Donor = donor; Knobs = knobs; DependsOn = dependsOn;
        }

        /// <summary>A locked section has no switch — every knob under it is always live.</summary>
        public bool IsLocked => GateKnob == null;
    }

    /// <summary>
    /// The flourish's pieces, declared once.
    ///
    /// <para>WHY A TABLE. The rig decides what to build, the Gather tab decides what to show
    /// and under which header, and the override bag decides what a spell owns — three readers
    /// of the same fact. Before this they each held their own copy: <c>BuildRig</c> hard-coded
    /// four guards, the panel grouped knobs by string prefix in <c>GroupOf</c>, and a fifth
    /// special case tied the funnel rows to <c>FunnelBands</c>. Four pieces plus one shared
    /// input all fell through the grouper's default into one header called "Body &amp; Light",
    /// and none of them looked like something a designer could switch off.</para>
    ///
    /// <para>The zero-sentinel convention is deliberate and uniform: a piece is OFF when its
    /// gate holds an enum <c>None</c> or a numeric zero, which means <b>no new serialized
    /// data</b> — the switch writes an ordinary entry into the same
    /// <see cref="CastGatherOverride"/> bag a knob pin uses, so undo, save and migration all
    /// come free. <c>fireball.asset</c> already shipped <c>{Sigil, None}</c> by hand before
    /// this existed; the switch writes byte-for-byte the same thing.</para>
    ///
    /// <para>Adding a tenth piece costs ONE row here, plus its guard in <c>BuildRig</c>.
    /// Forgetting the row is a red coverage test rather than a knob that silently renders
    /// under the wrong header — <c>CastFlourishPieceTableTests</c> fails in BOTH directions,
    /// the shape CLAUDE.md credits for keeping <c>FSMBuiltInTransitions</c> honest.</para>
    /// </summary>
    internal static class CastFlourishPieces
    {
        // Locked sections. Neither can honestly carry a switch, and saying so in the table is
        // what stops someone adding one later.
        //
        // Timing is the CLOCK, not a piece: Update divides by Gather and self-destructs at
        // Duration, so Duration = 0 builds the whole rig and destroys it on frame one — it
        // suppresses nothing. Anchor is a CHOICE, not a presence: HandAnchored picks the point
        // the lance and every mote approach ride, so switching it "off" relocates the gather
        // onto the body rather than removing anything.
        public static readonly CastFlourishPiece Timing = new CastFlourishPiece(
            "Timing", null, null, null,
            new[] { "Duration", "Gather", "Release" });

        public static readonly CastFlourishPiece Anchor = new CastFlourishPiece(
            "Anchor", null, null, null,
            new[] { "HandAnchored" });

        public static readonly CastFlourishPiece Sigil = new CastFlourishPiece(
            "Sigil", "Sigil", SigilMotion.None, CastFlourishFamilies.Hurl,
            new[] { "Sigil", "SigilRadius", "SigilSpin", "SigilAlpha" });

        public static readonly CastFlourishPiece Motes = new CastFlourishPiece(
            "Motes", "MoteCount", 0, CastFlourishFamilies.Hurl,
            new[] { "Approach", "Departure", "MoteCount", "MoteRadius",
                    "MoteSpeedMin", "MoteSpeedMax", "MoteSize", "MoteSpread" },
            dependsOn: "Funnel");

        public static readonly CastFlourishPiece Lance = new CastFlourishPiece(
            "Lance", "Lance", LanceAim.None, CastFlourishFamilies.Hurl,
            new[] { "Lance", "LanceLength" });

        public static readonly CastFlourishPiece Burst = new CastFlourishPiece(
            "Burst", "Burst", BurstOrigin.None, CastFlourishFamilies.Hurl,
            new[] { "Burst", "BurstRadius" });

        // Hurl authors FunnelBands = 0, so its donor has to be the one family that draws one.
        public static readonly CastFlourishPiece Funnel = new CastFlourishPiece(
            "Funnel", "FunnelBands", 0, CastFlourishFamilies.Vortex,
            new[] { "FunnelBands", "FunnelHeight", "FunnelBaseRadius",
                    "FunnelTopRadius", "FunnelSpin" });

        public static readonly CastFlourishPiece Aura = new CastFlourishPiece(
            "Aura", "AuraDrive", 0f, CastFlourishFamilies.Hurl, new[] { "AuraDrive" });

        public static readonly CastFlourishPiece Hand = new CastFlourishPiece(
            "Hand Glow", "HandScale", 0f, CastFlourishFamilies.Hurl, new[] { "HandScale" });

        public static readonly CastFlourishPiece Light = new CastFlourishPiece(
            "Light", "LightMul", 0f, CastFlourishFamilies.Hurl, new[] { "LightMul" });

        public static readonly CastFlourishPiece BodyTint = new CastFlourishPiece(
            "Body Tint", "BodyDrive", 0f, CastFlourishFamilies.Hurl, new[] { "BodyDrive" });

        /// <summary>Render order, locked sections first so the clock reads before the pieces.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of readonly structs built once from string " +
            "literals, sentinel values and static family functions. Holds no Unity objects and is " +
            "never mutated after construction, so it cannot go stale across a Play session.")]
        public static readonly IReadOnlyList<CastFlourishPiece> All = new[]
        {
            Timing, Anchor, Sigil, Motes, Lance, Burst, Funnel, Aura, Hand, Light, BodyTint,
        };

        /// <summary>
        /// Whether <paramref name="profile"/> draws this piece.
        ///
        /// <para>DERIVED, never stored. There is no persisted "enabled" bool that could drift
        /// out of step with the value the rig actually reads — the switch and the gate row are
        /// two views of one number.</para>
        /// </summary>
        public static bool IsOn(CastFlourishProfile profile, CastFlourishPiece piece)
            => piece.IsLocked
               || !Equals(CastGatherOverrides.Read(profile, piece.GateKnob), piece.OffValue);

        /// <summary>
        /// The section a knob belongs to. Replaces the old string-prefix grouper, whose default
        /// arm swept four separate pieces into one header.
        /// </summary>
        public static bool TryGetSection(string knobName, out CastFlourishPiece piece)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var candidate = All[i];
                for (int k = 0; k < candidate.Knobs.Length; k++)
                    if (string.Equals(candidate.Knobs[k], knobName, StringComparison.Ordinal))
                    {
                        piece = candidate;
                        return true;
                    }
            }
            piece = default;
            return false;
        }

        /// <summary>
        /// Write the OFF sentinel for a piece into an authored bag. Typed off the gate knob,
        /// so an enum gate stores a member NAME and a numeric one stores a number — the bag
        /// reads whichever the knob's type dictates.
        /// </summary>
        public static void WriteOff(CastGatherOverride overrides, CastFlourishPiece piece)
        {
            if (overrides == null || piece.IsLocked) return;

            var knob = CastGatherOverrides.Knob(piece.GateKnob);
            if (knob == null) return;

            if (knob.FieldType.IsEnum) overrides.SetText(piece.GateKnob, piece.OffValue.ToString());
            else overrides.SetNumber(piece.GateKnob, Convert.ToSingle(piece.OffValue));
        }

        /// <summary>
        /// Pin every knob this piece owns from <paramref name="donorProfile"/>. Used only when
        /// a piece is off because its FAMILY draws none — releasing the gate alone would leave
        /// the piece nominally on with the family's zeroed companions, i.e. a switch that says
        /// ON and draws nothing. Edge is the live example: it ships the sigil's radius, spin
        /// and alpha all at zero beside <c>Sigil = None</c>.
        /// </summary>
        public static void SeedFrom(CastGatherOverride overrides, CastFlourishPiece piece,
                                    CastFlourishProfile donorProfile)
        {
            if (overrides == null || piece.IsLocked) return;

            for (int i = 0; i < piece.Knobs.Length; i++)
            {
                string name = piece.Knobs[i];
                var knob = CastGatherOverrides.Knob(name);
                if (knob == null) continue;

                object value = knob.GetValue(donorProfile);
                if (knob.FieldType.IsEnum) overrides.SetText(name, value.ToString());
                else if (knob.FieldType == typeof(bool))
                    overrides.SetNumber(name, (bool)value ? 1f : 0f);
                else overrides.SetNumber(name, Convert.ToSingle(value));
            }
        }
    }
}
