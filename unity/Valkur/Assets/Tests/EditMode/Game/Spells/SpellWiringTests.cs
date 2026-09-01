using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Guards the four seams a spell has to cross to be castable, each of which fails
    /// silently on its own.
    ///
    /// A spell needs an executor registered for its <see cref="SpellType"/>, an asset the
    /// catalog actually lists (<c>EntitySetup.InitPlayerSpells</c> registers what the catalog
    /// holds, and only falls back to a folder scan when the catalog is EMPTY — so a spell
    /// missing from a non-empty catalog is simply never learned), and, if it is meant to be
    /// pressed, an <c>InputAction</c> that exists in the canonical asset. That last one is the
    /// loud failure of the four and the worst: <c>InputService</c> resolves every action with
    /// <c>throwIfNotFound: true</c> in its constructor, so a property added in C# without the
    /// matching action in the asset throws during bootstrap and takes ALL INPUT with it —
    /// keyboard, mouse and every editor hotkey — not just the spell that was added.
    /// </summary>
    public class SpellWiringTests
    {
        private const string SpellCatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";

        private static SpellCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            Assert.IsNotNull(catalog, $"SpellCatalog missing at {SpellCatalogPath}.");
            return catalog;
        }

        private static IEnumerable<SpellDefinition> AllSpellAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellFolder }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null) yield return spell;
            }
        }

        // ── Executors ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Enum values that have no executor ON PURPOSE, because no spell has ever been
        /// authored with them.
        ///
        /// <c>Trap</c> has no executor class at all; <c>Shield</c> has one
        /// (<c>ShieldExecutor</c>) but it is registered against <c>SphereMagicShield</c>,
        /// which is the shield that actually shipped. Both are dead values kept only because
        /// the enum is stored as an integer and removing one would renumber every value after
        /// it, repointing 47 assets at the wrong executor without touching a file.
        ///
        /// They are listed rather than tolerated: the test below still fails for any OTHER
        /// unregistered type, so adding a new one without wiring it stays a red test.
        /// </summary>
        private static readonly HashSet<SpellType> KnownExecutorless =
            new HashSet<SpellType> { SpellType.Trap, SpellType.Shield };

        [Test]
        public void EverySpellTypeUsedByAnAsset_HasAnExecutor()
        {
            var missing = new List<string>();
            foreach (SpellDefinition spell in AllSpellAssets())
            {
                if (SpellCaster.GetExecutor(spell.type) == null)
                    missing.Add($"'{spell.spellKey}' is {spell.type}");
            }

            // This is the half that bites: ExecuteSpell falls back to the PROJECTILE executor
            // for an unregistered type, so a spell that should draw a weapon or lay a trap
            // fires a fireball instead and logs a warning nobody reads mid-fight.
            Assert.IsEmpty(missing,
                "Shipped spells whose type has no executor — each silently fires a projectile " +
                "instead:\n" + string.Join("\n", missing));
        }

        [Test]
        public void NoUnexpectedSpellType_LacksAnExecutor()
        {
            var missing = new List<string>();
            foreach (SpellType type in Enum.GetValues(typeof(SpellType)))
            {
                if (SpellCaster.GetExecutor(type) == null && !KnownExecutorless.Contains(type))
                    missing.Add(type.ToString());
            }

            Assert.IsEmpty(missing,
                "SpellType values with no executor registered in SpellCaster.Executors. Add " +
                "the executor, or add the value to KnownExecutorless with the reason it will " +
                "never have one: " + string.Join(", ", missing));
        }

        [Test]
        public void EverySpellAsset_HasADefinedType()
        {
            var bad = new List<string>();
            foreach (SpellDefinition spell in AllSpellAssets())
            {
                if (!Enum.IsDefined(typeof(SpellType), spell.type))
                    bad.Add($"'{spell.spellKey}' → {(int)spell.type}");
            }

            // The enum is stored as an integer, so REORDERING it repoints every shipped asset
            // at a different executor without touching a file. New values go on the end.
            Assert.IsEmpty(bad,
                "Spell assets whose type is not a defined SpellType value. Adding an enum " +
                "member anywhere but the end does this to every asset after it:\n" +
                string.Join("\n", bad));
        }

        // ── Catalog membership ───────────────────────────────────────────────────────

        [Test]
        public void EverySpellAsset_IsListedInTheCatalog()
        {
            SpellCatalog catalog = LoadCatalog();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpellDefinition spell in catalog.AllSpells)
            {
                if (spell != null && !string.IsNullOrEmpty(spell.spellKey))
                    listed.Add(spell.spellKey);
            }

            var orphans = new List<string>();
            foreach (SpellDefinition spell in AllSpellAssets())
            {
                if (!string.IsNullOrEmpty(spell.spellKey) && !listed.Contains(spell.spellKey))
                    orphans.Add(spell.spellKey);
            }

            Assert.IsEmpty(orphans,
                "Spell assets that exist but are not in SpellCatalog. EntitySetup registers " +
                "what the CATALOG holds and only scans the folder when the catalog is empty, " +
                "so these are never learned by any player:\n" + string.Join("\n", orphans));
        }

        [Test]
        public void TheCatalog_HasNoNullOrDuplicateEntries()
        {
            SpellCatalog catalog = LoadCatalog();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int nulls = 0;

            SpellDefinition[] spells = catalog.AllSpells;
            for (int i = 0; i < spells.Length; i++)
            {
                if (spells[i] == null) { nulls++; continue; }
                string key = spells[i].spellKey;
                if (string.IsNullOrEmpty(key)) continue;
                seen.TryGetValue(key, out int count);
                seen[key] = count + 1;
            }

            var dupes = new List<string>();
            foreach (KeyValuePair<string, int> pair in seen)
            {
                if (pair.Value > 1) dupes.Add($"{pair.Key} x{pair.Value}");
            }

            Assert.AreEqual(0, nulls, $"SpellCatalog holds {nulls} null entries.");
            Assert.IsEmpty(dupes,
                "Duplicate spellKeys in SpellCatalog. RegisterSpell keys a dictionary by " +
                "spellKey, so the later one silently wins:\n" + string.Join("\n", dupes));
        }

        // ── The weapon-loadout spell ─────────────────────────────────────────────────

        [Test]
        public void WeaponLoadoutSpells_NameALoadoutSomeCharacterDeclares()
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayerCatalog }))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def?.assetConfig?.loadouts == null) continue;
                foreach (Loadout loadout in def.assetConfig.loadouts)
                {
                    if (loadout != null && !string.IsNullOrEmpty(loadout.key))
                        declared.Add(loadout.key);
                }
            }

            var problems = new List<string>();
            int found = 0;
            foreach (SpellDefinition spell in AllSpellAssets())
            {
                if (spell.type != SpellType.WeaponLoadout) continue;
                found++;

                if (string.IsNullOrEmpty(spell.loadoutKey))
                {
                    problems.Add($"'{spell.spellKey}' has an empty loadoutKey, so it can only " +
                                 "ever do nothing");
                }
                else if (declared.Count > 0 && !declared.Contains(spell.loadoutKey))
                {
                    problems.Add($"'{spell.spellKey}' names loadout '{spell.loadoutKey}', which " +
                                 "no PlayerDefinition declares. PlayerLoadoutController refuses " +
                                 "an unknown key rather than reading it as 'unequip', so the " +
                                 "spell burns its cooldown and does nothing");
                }
            }

            if (found == 0)
                Assert.Ignore("No WeaponLoadout spells shipped.");
            if (declared.Count == 0)
                Assert.Ignore("No character declares a loadout yet — run the frame importer " +
                              "before this assertion means anything.");

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // ── Input ────────────────────────────────────────────────────────────────────

        private static InputActionAsset CanonicalAsset()
        {
            var asset = Resources.Load<InputActionAsset>(InputService.CanonicalAssetResourcePath);
            Assert.IsNotNull(asset,
                $"Canonical asset missing at Resources/{InputService.CanonicalAssetResourcePath}. " +
                "The whole input pipeline bootstraps from it.");
            return asset;
        }

        /// <summary>
        /// Reproduces exactly what <c>InputService.Initialize</c> does, and is the reason this
        /// test exists at all: each action class resolves every one of its actions with
        /// <c>throwIfNotFound: true</c> in its constructor. A property added in C# without the
        /// matching action in the asset therefore throws during BOOTSTRAP and takes ALL input
        /// with it — keyboard, mouse and every editor hotkey — not just the binding that was
        /// added. Constructing the three classes here turns that into a red test instead of a
        /// dead game.
        /// </summary>
        [TestCase("UI")]
        [TestCase("Gameplay")]
        [TestCase("Editors")]
        public void EveryActionInputServiceResolves_ExistsInTheCanonicalAsset(string mapName)
        {
            InputActionAsset asset = CanonicalAsset();
            InputActionMap map = asset.FindActionMap(mapName);
            Assert.IsNotNull(map, $"The canonical asset has no '{mapName}' action map.");

            Assert.DoesNotThrow(() =>
            {
                switch (mapName)
                {
                    case "UI":       _ = new InputService.UIActions(map); break;
                    case "Gameplay": _ = new InputService.GameplayActions(map); break;
                    case "Editors":  _ = new InputService.EditorsActions(map); break;
                }
            },
            $"Resolving the '{mapName}' map threw. An action InputService asks for is not in " +
            "ValkurInputActions.inputactions — add it there (and a binding), not only in C#.");
        }

        [Test]
        public void EveryBoundSpellKey_ResolvesToASpellInTheCatalog()
        {
            SpellCatalog catalog = LoadCatalog();
            InputActionMap gameplay = CanonicalAsset().FindActionMap("Gameplay");
            Assert.IsNotNull(gameplay);

            var actions = new InputService.GameplayActions(gameplay);

            var unknown = new List<string>();
            foreach ((InputAction action, string spellKey, KeyCode legacy) in
                     actions.EnumerateSpellBindings())
            {
                if (string.IsNullOrEmpty(spellKey)) continue;
                if (catalog.GetByKey(spellKey) == null)
                    unknown.Add($"{action?.name ?? "?"} / {legacy} → '{spellKey}'");
            }

            // EnumerateSpellBindings is the single source of truth for what a key press casts.
            // A key naming a spell nothing answers to is swallowed by TryCastByKey's dictionary
            // miss, with no log and no visible effect — indistinguishable from a dead key.
            Assert.IsEmpty(unknown,
                "Key bindings that cast a spellKey no catalog spell answers to:\n" +
                string.Join("\n", unknown));
        }

        [Test]
        public void EverySpellBinding_HasAKeyboardBinding()
        {
            InputActionMap gameplay = CanonicalAsset().FindActionMap("Gameplay");
            var actions = new InputService.GameplayActions(gameplay);

            var unbound = new List<string>();
            foreach ((InputAction action, string spellKey, KeyCode legacy) in
                     actions.EnumerateSpellBindings())
            {
                if (action != null && action.bindings.Count == 0)
                    unbound.Add($"{action.name} ('{spellKey}')");
            }

            Assert.IsEmpty(unbound,
                "Spell actions that exist but carry no binding. The action resolves, so " +
                "nothing throws; the key simply never fires and only the legacy KeyCode " +
                "fallback still works:\n" + string.Join("\n", unbound));
        }
    }
}
