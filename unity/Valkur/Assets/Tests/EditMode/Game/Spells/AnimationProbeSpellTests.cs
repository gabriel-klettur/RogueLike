using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Guards the animation probes: one <see cref="SpellType.AnimationProbe"/> spell per
    /// authored animation, so every one of them can be selected and watched in the Spells
    /// Editor — including the states no gameplay spell will ever enter, because locomotion,
    /// the damage flow and the death flow own them rather than casting.
    ///
    /// The thing that rots here is coverage. A new animation arrives with a wave and nothing
    /// forces a probe to arrive with it, so the animation ships unwatchable and nobody
    /// notices until they go looking for it. This test reads the SPRITE FOLDERS on disk —
    /// which is what a wave actually produces — and fails when one has no probe.
    ///
    /// It also pins what a probe must NOT be. The moment one carries damage or a mana cost it
    /// has stopped being a diagnostic and started being a spell, and a spell needs balancing.
    /// </summary>
    public class AnimationProbeSpellTests
    {
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";
        private const string DwarfArt = "Assets/_Project/Art/Characters/dwarf";
        private const string DwarfDefinition = "Assets/_Project/Data/Catalogs/Players/dwarf.asset";
        private const string SpellCatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";

        /// <summary>The manifest's state names — the same vocabulary
        /// <c>SpellPreviewService.TryParseAnimState</c> accepts.</summary>
        private static readonly string[] ValidStates =
            { "idle", "walk", "chase", "cast", "attack", "damage", "death", "recover" };

        private static IEnumerable<SpellDefinition> Probes()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellFolder }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null && spell.type == SpellType.AnimationProbe)
                    yield return spell;
            }
        }

        [Test]
        public void EveryProbe_NamesAValidPreviewState()
        {
            var bad = new List<string>();
            foreach (SpellDefinition probe in Probes())
            {
                if (string.IsNullOrEmpty(probe.animState))
                {
                    bad.Add($"'{probe.spellKey}' names no state, so it previews the default " +
                            "cast pose and probes nothing");
                    continue;
                }
                if (Array.IndexOf(ValidStates, probe.animState.Trim().ToLowerInvariant()) < 0)
                    bad.Add($"'{probe.spellKey}' names '{probe.animState}', which the " +
                            "preview cannot parse and silently falls back to Cast for");
            }

            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void EveryProbe_IsInert()
        {
            var bad = new List<string>();
            foreach (SpellDefinition probe in Probes())
            {
                if (probe.damage != 0f) bad.Add($"'{probe.spellKey}' deals {probe.damage} damage");
                if (probe.manaCost != 0f) bad.Add($"'{probe.spellKey}' costs {probe.manaCost} mana");
                if (probe.damagePerTick != 0f) bad.Add($"'{probe.spellKey}' ticks damage");
                if (probe.healPerTick != 0f) bad.Add($"'{probe.spellKey}' heals");
            }

            // A probe that does something is no longer a diagnostic. Its executor is empty, so
            // these values would not even fire — they would just be a lie in the Inspector for
            // the next person reading it.
            Assert.IsEmpty(bad,
                "AnimationProbe spells must be inert:\n" + string.Join("\n", bad));
        }

        [Test]
        public void EveryProbe_IsListedInTheCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            Assert.IsNotNull(catalog);

            var missing = new List<string>();
            foreach (SpellDefinition probe in Probes())
            {
                if (catalog.GetByKey(probe.spellKey) == null) missing.Add(probe.spellKey);
            }

            // The Spells Editor's picker enumerates SpellCatalog.GetAllKeys(). A probe outside
            // the catalog exists on disk and is unreachable from the one screen it was made for.
            Assert.IsEmpty(missing,
                "Probes missing from SpellCatalog, so the Spells Editor cannot list them:\n" +
                string.Join("\n", missing));
        }

        [Test]
        public void EveryDwarfAnimationFolder_HasAProbe()
        {
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", DwarfArt));
            if (!Directory.Exists(abs))
                Assert.Ignore($"No dwarf art at {abs}.");

            var probeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpellDefinition probe in Probes()) probeKeys.Add(probe.spellKey);

            var uncovered = new List<string>();
            foreach (string folder in Directory.GetDirectories(abs))
            {
                string state = Path.GetFileName(folder);
                if (!probeKeys.Contains("anim_" + state)) uncovered.Add(state);
            }

            // Keyed off the FOLDERS because that is what a wave produces: the pipeline writes
            // one per animation state, so a new animation shows up here the moment it ships
            // rather than whenever someone remembers to look for it.
            Assert.IsEmpty(uncovered,
                "Dwarf animations with no 'anim_<state>' probe — each is unwatchable in the " +
                "Spells Editor:\n" + string.Join("\n", uncovered));
        }

        /// <summary>
        /// The end-to-end one: build the rig the Spells Editor builds, apply each probe the
        /// way the editor applies it, and assert the sprite that lands is the probe's OWN
        /// animation family.
        ///
        /// This is the test that catches what the per-field checks cannot. Every probe can
        /// name a valid state, be listed in the catalog and reserve a variant, and still show
        /// the wrong art — which is exactly what the three `anim_armed_*` probes did: a
        /// loadout's locomotion only exists while the loadout is worn, so they rendered the
        /// UNARMED idle, walk and run while every other assertion about them passed.
        /// </summary>
        [Test]
        public void EveryProbe_RendersItsOwnAnimation()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DwarfDefinition);
            Assert.IsNotNull(def?.assetConfig);

            var created = new List<UnityEngine.Object>();
            try
            {
                // The live player, as EntitySetup builds it.
                var playerGo = new GameObject("ProbeTestPlayer");
                created.Add(playerGo);
                playerGo.AddComponent<SpriteRenderer>();
                Assert.IsTrue(Valkur.Gameplay.EntityAnimationBinder.ApplyPlayerVisuals(playerGo, def));
                var loadouts = playerGo.AddComponent<Valkur.Gameplay.PlayerLoadoutController>();
                loadouts.Initialize(def.assetConfig);
                var playerAnim = playerGo.GetComponent<Valkur.Gameplay.DirectionalAnimator>();

                var wrong = new List<string>();
                foreach (SpellDefinition probe in Probes())
                {
                    string rendered = RenderProbe(probe, playerAnim, loadouts, created);
                    string family = "dwarf_" + probe.spellKey.Substring("anim_".Length) + "_";
                    if (rendered == null || !rendered.StartsWith(family, StringComparison.Ordinal))
                        wrong.Add($"'{probe.spellKey}' rendered '{rendered ?? "nothing"}', expected {family}*");
                }

                Assert.IsEmpty(wrong, string.Join("\n", wrong));
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        /// <summary>
        /// Mirrors <c>SpellPreviewService.ApplyCharacterState</c> + <c>ApplyCharacterDirection</c>:
        /// a fresh rig per spell (the editor rebuilds the caster on every selection), the seven
        /// base sets copied from the live player, the variant tables copied on top, the preview
        /// loadout applied, then the state and variant the spell resolves to.
        /// </summary>
        private static string RenderProbe(SpellDefinition probe,
                                          Valkur.Gameplay.DirectionalAnimator playerAnim,
                                          Valkur.Gameplay.PlayerLoadoutController loadouts,
                                          List<UnityEngine.Object> created)
        {
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var rendererField = typeof(Valkur.Gameplay.DirectionalAnimator)
                .GetField("targetRenderer", Instance);

            var go = new GameObject("ProbeTestPreview");
            created.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<Valkur.Gameplay.DirectionalAnimator>();
            // Awake never runs in EditMode, so the renderer the animator draws through is wired
            // by hand — the same workaround every animator test here uses.
            rendererField.SetValue(anim, sr);

            anim.SetSpriteSets(playerAnim.IdleSprites, playerAnim.WalkSprites, playerAnim.ChaseSprites,
                               playerAnim.CastSprites, playerAnim.AttackSprites, playerAnim.DamageSprites,
                               playerAnim.DeathSprites, playerAnim.PrefersCardinalDirectionSampling);
            anim.CopyVariantsFrom(playerAnim);

            if (!string.IsNullOrEmpty(probe.loadoutAnimKey) &&
                loadouts.Config != null &&
                loadouts.Config.FindLoadout(probe.loadoutAnimKey) != null)
            {
                Valkur.Gameplay.EntityAnimationBinder.ApplyLoadout(
                    go, loadouts.Config, probe.loadoutAnimKey);
                anim = go.GetComponent<Valkur.Gameplay.DirectionalAnimator>();
                rendererField.SetValue(anim, sr);
            }

            var state = ParseState(probe.animState);
            int variant = anim.VariantForSpell(state, probe.spellKey);
            anim.SetState(state, Valkur.Gameplay.DirectionalAnimator.Direction.East, variant);
            anim.RestartCurrentState();

            return sr.sprite != null ? sr.sprite.name : null;
        }

        private static Valkur.Gameplay.DirectionalAnimator.AnimState ParseState(string name)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "idle":    return Valkur.Gameplay.DirectionalAnimator.AnimState.Idle;
                case "walk":    return Valkur.Gameplay.DirectionalAnimator.AnimState.Walk;
                case "chase":   return Valkur.Gameplay.DirectionalAnimator.AnimState.Chase;
                case "attack":  return Valkur.Gameplay.DirectionalAnimator.AnimState.Attack;
                case "damage":  return Valkur.Gameplay.DirectionalAnimator.AnimState.Damage;
                case "death":   return Valkur.Gameplay.DirectionalAnimator.AnimState.Death;
                case "recover": return Valkur.Gameplay.DirectionalAnimator.AnimState.Recover;
                default:        return Valkur.Gameplay.DirectionalAnimator.AnimState.Cast;
            }
        }

        /// <summary>
        /// The one that matters most: what a probe renders when it is CAST IN THE GAME, which
        /// is what left click does while the Spells Editor is open (the editor redirects the
        /// primary cast — see <c>PlayerController.PollRedirectedPrimaryCast</c>).
        ///
        /// This is a different path from the preview panel and it was broken for nine of the
        /// nineteen probes: <c>TriggerCastAnimation</c> resolved the state from
        /// <c>usesAttackAnimation</c> alone, so every probe naming idle/walk/chase/damage/
        /// death/recover fell through to Cast, reserved no cast variant, and took whatever
        /// <c>NextVariant</c> handed it — a rotating spellcast. Selecting "Anim: Die" cast a
        /// spellcasting animation.
        /// </summary>
        [Test]
        public void EveryProbe_RendersItsOwnAnimation_WhenCastInGame()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DwarfDefinition);
            Assert.IsNotNull(def?.assetConfig);

            var created = new List<UnityEngine.Object>();
            try
            {
                var wrong = new List<string>();
                foreach (SpellDefinition probe in Probes())
                {
                    string rendered = RenderProbeAsCast(probe, def, created);
                    string family = "dwarf_" + probe.spellKey.Substring("anim_".Length) + "_";
                    if (rendered == null || !rendered.StartsWith(family, StringComparison.Ordinal))
                        wrong.Add($"'{probe.spellKey}' cast in game rendered '{rendered ?? "nothing"}', expected {family}*");
                }
                Assert.IsEmpty(wrong, string.Join("\n", wrong));
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        /// <summary>
        /// Mirrors the cast path: AnimationProbeExecutor runs first (inside TryCastByKey) and
        /// puts the caster into the animation's loadout, then TriggerCastAnimation resolves
        /// the state from <c>animState</c> and the variant from the character's reservations.
        /// </summary>
        private static string RenderProbeAsCast(SpellDefinition probe, PlayerDefinition def,
                                                List<UnityEngine.Object> created)
        {
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var rendererField = typeof(Valkur.Gameplay.DirectionalAnimator)
                .GetField("targetRenderer", Instance);

            var go = new GameObject("ProbeCastTarget");
            created.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            Assert.IsTrue(Valkur.Gameplay.EntityAnimationBinder.ApplyPlayerVisuals(go, def));

            var loadouts = go.AddComponent<Valkur.Gameplay.PlayerLoadoutController>();
            loadouts.Initialize(def.assetConfig);
            if (!string.IsNullOrEmpty(probe.loadoutAnimKey) && loadouts.HasLoadout(probe.loadoutAnimKey))
                loadouts.SetLoadout(probe.loadoutAnimKey);

            var anim = go.GetComponent<Valkur.Gameplay.DirectionalAnimator>();
            rendererField.SetValue(anim, sr);

            var state = ParseState(probe.animState);
            int variant = anim.VariantForSpell(state, probe.spellKey);
            anim.SetState(state, Valkur.Gameplay.DirectionalAnimator.Direction.East, variant);
            anim.RestartCurrentState();

            return sr.sprite != null ? sr.sprite.name : null;
        }

        /// <summary>
        /// No animation state a probe can ask for may strand the player in it.
        ///
        /// The rule <c>AnimState.Recover</c>'s own doc states: a state locomotion refuses to
        /// override and nothing reverts is a soft lock. Letting spells name ANY state made
        /// that reachable from a spell for the first time — a probe asking for `death` would
        /// have held the corpse pose forever.
        /// </summary>
        [Test]
        public void NoAnimationState_StrandsThePlayerWhenTheCastWindowExpires()
        {
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DwarfDefinition);
            Assert.IsNotNull(def?.assetConfig);

            var pcType = typeof(Valkur.Gameplay.PlayerController);
            var revert = pcType.GetMethod("TickCastAnimRevert", Instance);
            var animField = pcType.GetField("_animator", Instance);
            var endField = pcType.GetField("_castAnimEndTime", Instance);
            var stateField = pcType.GetField("_castAnimState", Instance);
            var facingField = pcType.GetField("_facingDirection", Instance);
            Assert.IsNotNull(revert, "TickCastAnimRevert is gone.");
            Assert.IsNotNull(stateField, "_castAnimState is gone — the revert can no longer be general.");

            var created = new List<UnityEngine.Object>();
            var stranded = new List<string>();
            try
            {
                foreach (Valkur.Gameplay.DirectionalAnimator.AnimState state in
                         Enum.GetValues(typeof(Valkur.Gameplay.DirectionalAnimator.AnimState)))
                {
                    var go = new GameObject("RevertTarget");
                    created.Add(go);
                    go.AddComponent<SpriteRenderer>();
                    Valkur.Gameplay.EntityAnimationBinder.ApplyPlayerVisuals(go, def);
                    var pc = go.AddComponent<Valkur.Gameplay.PlayerController>();
                    var anim = go.GetComponent<Valkur.Gameplay.DirectionalAnimator>();

                    animField.SetValue(pc, anim);
                    facingField.SetValue(pc, Vector2.right);
                    anim.SetState(state, Valkur.Gameplay.DirectionalAnimator.Direction.East);
                    stateField.SetValue(pc, state);

                    // The window has just expired. Clamped above zero because zero is the
                    // "no window open" sentinel TickCastAnimRevert early-returns on, and in
                    // Edit Mode Time.time is the time since the last domain reload — a test
                    // run starts with one, so it is routinely under 0.01 and the naive
                    // subtraction goes negative. That made the revert no-op and reported
                    // every state in the enum as a soft lock, at random, depending only on
                    // how long the editor had been idle before the run.
                    endField.SetValue(pc, Mathf.Max(float.Epsilon, Time.time - 0.01f));
                    revert.Invoke(pc, null);

                    var after = anim.CurrentState;
                    bool freed = after == Valkur.Gameplay.DirectionalAnimator.AnimState.Idle
                              || after == Valkur.Gameplay.DirectionalAnimator.AnimState.Walk;
                    if (!freed) stranded.Add($"{state} -> {after}");
                }

                Assert.IsEmpty(stranded,
                    "States the player is never released from — each is a soft lock:\n" +
                    string.Join("\n", stranded));
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        [Test]
        public void EveryVariantBackedProbe_ResolvesItsOwnVariant()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(DwarfDefinition);
            Assert.IsNotNull(def?.assetConfig);

            // A probe whose state carries variants must be RESERVED on one, or it previews
            // whatever the base set holds and shows the same pose as its neighbours.
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in def.assetConfig.castVariants)
                if (v?.spellKeys != null) foreach (string k in v.spellKeys) claimed.Add(k);
            foreach (var v in def.assetConfig.attackVariants)
                if (v?.spellKeys != null) foreach (string k in v.spellKeys) claimed.Add(k);

            var unpinned = new List<string>();
            foreach (SpellDefinition probe in Probes())
            {
                string state = probe.animState?.Trim().ToLowerInvariant();
                bool stateCarriesVariants = state == "cast" || state == "attack";
                if (!stateCarriesVariants) continue;

                if (!claimed.Contains(probe.spellKey)) unpinned.Add(probe.spellKey);
            }

            Assert.IsEmpty(unpinned,
                "Probes that preview a state WITH variants but reserve none, so they show the " +
                "base pose instead of their own animation:\n" + string.Join("\n", unpinned));
        }
    }
}
