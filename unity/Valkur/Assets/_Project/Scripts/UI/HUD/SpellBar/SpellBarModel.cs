using System;
using System.Collections.Generic;
using System.Text;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.UI.HUD
{
    /// <summary>What a slot on the bar stands for.</summary>
    public enum SpellBarEntryKind
    {
        /// <summary>A spell the character knows, cast by its own key.</summary>
        Spell = 0,
        /// <summary>A non-combat action: open the bag, talk, read the map.</summary>
        Verb = 1,
        /// <summary>The posture switch, last on both faces.</summary>
        Stance = 2,
    }

    /// <summary>One slot of a face, before anything is drawn.</summary>
    public readonly struct SpellBarEntry
    {
        public readonly SpellBarEntryKind Kind;

        /// <summary>The spell key for a spell, the verb id for a verb, "stance" for the switch.</summary>
        public readonly string Key;

        /// <summary>The catalog action (<c>Map/Action</c>) whose binding the key cap shows, or empty.</summary>
        public readonly string ActionId;

        /// <summary>Slots with the same group sit together; a new group opens a wider gap.</summary>
        public readonly int Group;

        public SpellBarEntry(SpellBarEntryKind kind, string key, string actionId, int group)
        {
            Kind = kind;
            Key = key ?? "";
            ActionId = actionId ?? "";
            Group = group;
        }

        public override string ToString() => Kind + ":" + Key;
    }

    /// <summary>One verb the Peace face may offer, as data.</summary>
    public readonly struct SpellBarVerbSpec
    {
        public readonly string Id;
        public readonly string Title;
        public readonly SpellBarGlyph Glyph;

        /// <summary>The Gameplay action it answers to, or empty for a verb reached only by click.</summary>
        public readonly string Action;

        public readonly int Group;

        public SpellBarVerbSpec(string id, string title, SpellBarGlyph glyph, string action, int group)
        {
            Id = id;
            Title = title;
            Glyph = glyph;
            Action = action ?? "";
            Group = group;
        }

        public string ActionId => string.IsNullOrEmpty(Action) ? "" : InputActionCatalog.MapGameplay + "/" + Action;
    }

    /// <summary>
    /// Which slots each posture's face shows. Pure, so the whole decision is testable without a
    /// canvas, a player or an InputService.
    ///
    /// <para><b>The two faces answer "what can I do RIGHT NOW".</b> War shows the spells the
    /// character knows that a key of theirs actually casts — the old bar printed 24 keys of which
    /// 24 were wrong. Peace shows the everyday verbs, and the rule that decides membership is the
    /// same one that decides whether the key WORKS: <see cref="InputContextPolicy.IsLive(InputActionDescriptor, Stance)"/>.
    /// So a verb the player silenced for Peace in the Controls editor leaves the face, and nothing
    /// that reaches the damage path can ever be on it — the policy refuses those in Peace at read
    /// time, whatever <c>controls.json</c> says.</para>
    ///
    /// <para>The posture switch is the last slot of BOTH faces, because it is the only way from
    /// one to the other and a face that did not show its own exit would be the soft lock the
    /// stance layer is built to avoid.</para>
    /// </summary>
    public static class SpellBarModel
    {
        public const string StanceKey = "stance";
        public const string StanceActionId = InputActionCatalog.MapGameplay + "/ToggleStance";

        /// <summary>Group index the posture switch always takes: after every other group.</summary>
        public const int StanceGroup = 1000;

        /// <summary>The Peace verbs, in the order the face shows them.</summary>
        [SelfHealingStatic("Immutable table of value structs built from constants. Holds no Unity object and is never mutated, so it cannot carry a destroyed reference across a Play session.")]
        public static readonly SpellBarVerbSpec[] PeaceVerbs =
        {
            new SpellBarVerbSpec("interact",  "Interactuar",    SpellBarGlyph.Interact,  "Interact",     0),
            new SpellBarVerbSpec("inventory", "Inventario",     SpellBarGlyph.Inventory, "Inventory",    1),
            new SpellBarVerbSpec("map",       "Mapa del mundo", SpellBarGlyph.Map,       "OpenWorldMap", 1),
            new SpellBarVerbSpec("crafting",  "Oficios",        SpellBarGlyph.Crafting,  "",             2),
            new SpellBarVerbSpec("quests",    "Misiones",       SpellBarGlyph.Quests,    "",             2),
            new SpellBarVerbSpec("talents",   "Talentos",       SpellBarGlyph.Talents,   "",             2),
            new SpellBarVerbSpec("grimoire",  "Grimorio",       SpellBarGlyph.Grimoire,  "",             2),
        };

        /// <summary>
        /// The War face: every catalog spell action whose spell the character knows and whose
        /// key is live in War, in catalog order (1..0 first, then the letters), grouped in
        /// <paramref name="groupSize"/>s, then the posture switch.
        /// </summary>
        public static List<SpellBarEntry> War(IEnumerable<InputActionDescriptor> spellActions,
                                              Func<string, bool> knows, int groupSize,
                                              Func<InputActionDescriptor, bool> liveInWar = null)
        {
            var list = new List<SpellBarEntry>();
            if (groupSize < 1) groupSize = 1;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (spellActions != null)
            {
                foreach (var d in spellActions)
                {
                    if (d == null || !d.IsSpell) continue;
                    if (!seen.Add(d.PayloadKey)) continue;
                    if (knows == null || !knows(d.PayloadKey)) continue;
                    bool live = liveInWar != null ? liveInWar(d) : InputContextPolicy.IsLive(d, Stance.War);
                    if (!live) continue;
                    list.Add(new SpellBarEntry(SpellBarEntryKind.Spell, d.PayloadKey, d.Id, list.Count / groupSize));
                }
            }
            list.Add(new SpellBarEntry(SpellBarEntryKind.Stance, StanceKey, StanceActionId, StanceGroup));
            return list;
        }

        /// <summary>
        /// The Peace face: every verb whose surface exists in this scene and whose key (when it
        /// has one) is live in Peace, then the posture switch.
        /// </summary>
        public static List<SpellBarEntry> Peace(Func<string, bool> verbExists,
                                                Func<InputActionDescriptor, bool> liveInPeace = null)
        {
            var list = new List<SpellBarEntry>();
            for (int i = 0; i < PeaceVerbs.Length; i++)
            {
                var v = PeaceVerbs[i];
                if (verbExists != null && !verbExists(v.Id)) continue;
                if (!string.IsNullOrEmpty(v.Action))
                {
                    var d = InputActionCatalog.Find(v.ActionId);
                    if (d == null) continue;
                    // Belt and braces: the policy already refuses a damage action in Peace, and
                    // this makes the face refuse it even if the policy is ever handed a lie.
                    if (d.ReachesDamage) continue;
                    bool live = liveInPeace != null ? liveInPeace(d) : InputContextPolicy.IsLive(d, Stance.Peace);
                    if (!live) continue;
                }
                list.Add(new SpellBarEntry(SpellBarEntryKind.Verb, v.Id, v.ActionId, v.Group));
            }
            list.Add(new SpellBarEntry(SpellBarEntryKind.Stance, StanceKey, StanceActionId, StanceGroup));
            return list;
        }

        /// <summary>The verb spec for an id, when there is one.</summary>
        public static bool TryGetVerb(string id, out SpellBarVerbSpec spec)
        {
            for (int i = 0; i < PeaceVerbs.Length; i++)
                if (PeaceVerbs[i].Id == id) { spec = PeaceVerbs[i]; return true; }
            spec = default;
            return false;
        }

        /// <summary>A string that changes exactly when the face would draw different slots.</summary>
        public static string Signature(Stance face, IReadOnlyList<SpellBarEntry> entries)
        {
            var sb = new StringBuilder(64);
            sb.Append(face == Stance.Peace ? 'P' : 'W');
            if (entries != null)
                for (int i = 0; i < entries.Count; i++) sb.Append('|').Append(entries[i].Key);
            return sb.ToString();
        }
    }
}
