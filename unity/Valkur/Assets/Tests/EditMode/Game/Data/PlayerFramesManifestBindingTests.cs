using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Asserts that what the frame pipeline PRODUCED is what the shipped
    /// <see cref="PlayerDefinition"/> assets actually BIND.
    ///
    /// This is the test that was missing. The pipeline and the ScriptableObjects are joined
    /// by a manual step — `Valkur > Players > Import Frame Sheets (Apply)` — and until it is
    /// run the sprites sit on disk, correct and complete, bound to nothing. Every symptom of
    /// that is silent and looks like a code bug: a reserved spell plays the wrong animation
    /// because its variant does not exist yet, a loadout toggle reports "this character
    /// declares no loadouts", a new state renders its neighbour through the fallback chain.
    /// Each one reads as "the feature is broken" rather than "the data was never imported",
    /// and each costs a round of in-game debugging to tell apart.
    ///
    /// So the failure message names the fix. A red test here means run the importer — it does
    /// NOT mean the manifest or the assets are wrong.
    ///
    /// Deliberately asserts SHAPE, not sprites: which states, variants, loadouts and
    /// reservations exist, and how many frames each carries. Which sprite lands in which
    /// bucket is <see cref="PlayerTwoDirectionRigTests"/>'s job, and asserting the same thing
    /// twice would make both tests fail for one cause.
    /// </summary>
    public class PlayerFramesManifestBindingTests
    {
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";
        private const string ManifestGlob = "player_frames_manifest*.json";

        /// <summary>Manifest state name → the list on <see cref="EntityAssetConfig"/> it binds
        /// into. The importer's own mapping, restated here on purpose: a test that reused the
        /// importer's private switch could not catch that switch being wrong.</summary>
        private static List<Sprite> SheetsFor(EntityAssetConfig config, string state)
        {
            switch (state)
            {
                case "idle":    return config.idleSheets;
                case "walk":    return config.walkSheets;
                case "chase":   return config.chaseSheets;
                case "cast":    return config.castSheets;
                case "attack":  return config.attackSheets;
                case "damage":  return config.damageSheets;
                case "death":   return config.deathSheets;
                case "recover": return config.recoverSheets;
                default:        return null;
            }
        }

        // ── Manifest schema (JsonUtility needs concrete serializable types) ──────────

        [Serializable] private class Manifest { public List<PlayerEntry> players = new List<PlayerEntry>(); }

        [Serializable]
        private class PlayerEntry
        {
            public string playerKey;
            public List<SheetEntry> states = new List<SheetEntry>();
            public List<SheetEntry> attackVariants = new List<SheetEntry>();
            public List<SheetEntry> castVariants = new List<SheetEntry>();
            public List<LoadoutEntry> loadouts = new List<LoadoutEntry>();
        }

        [Serializable]
        private class SheetEntry
        {
            public string state;
            public string key;
            public int framesPerDirection;
            public List<string> sprites = new List<string>();
            public List<string> spellKeys = new List<string>();
            public string Name => string.IsNullOrEmpty(state) ? key : state;
        }

        [Serializable]
        private class LoadoutEntry
        {
            public string key;
            public List<SheetEntry> states = new List<SheetEntry>();
        }

        private static string ManifestDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..",
                                          "tools", "atlas", "generated"));

        private static IEnumerable<(string file, PlayerEntry entry)> ManifestEntries()
        {
            if (!Directory.Exists(ManifestDirectory))
                yield break;

            foreach (string path in Directory.GetFiles(ManifestDirectory, ManifestGlob))
            {
                Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                if (manifest?.players == null) continue;

                foreach (PlayerEntry entry in manifest.players)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.playerKey))
                        yield return (Path.GetFileName(path), entry);
                }
            }
        }

        private static PlayerDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayerCatalog}/{key}.asset");

        /// <summary>Appended to every failure, because the fix is the same for all of them.</summary>
        private const string RunTheImporter =
            "\n\nIf this is the only kind of failure here, the frames on disk have not been " +
            "bound yet: run 'Valkur > Players > Import Frame Sheets (Apply)'. It is idempotent " +
            "and leaves authored combat data, reservations and pacing untouched.";

        [Test]
        public void ManifestDirectory_Exists()
        {
            Assert.IsTrue(Directory.Exists(ManifestDirectory),
                $"No manifest directory at '{ManifestDirectory}'. The record of how every " +
                "player was built lives there and the source sheets under staging/ are " +
                "gitignored, so it is the only thing that survives them.");
            Assert.IsNotEmpty(Directory.GetFiles(ManifestDirectory, ManifestGlob),
                $"No '{ManifestGlob}' under '{ManifestDirectory}'.");
        }

        [Test]
        public void EveryManifestPlayer_HasADefinition()
        {
            var missing = new List<string>();
            foreach ((string file, PlayerEntry entry) in ManifestEntries())
            {
                if (Load(entry.playerKey) == null)
                    missing.Add($"{file}: '{entry.playerKey}'");
            }

            Assert.IsEmpty(missing,
                "A manifest names a player class with no PlayerDefinition. The importer " +
                "reports and skips these — it never creates a definition, because a class is " +
                "a design entity with stats and a slot in character select, none of which an " +
                "art import can invent.\n" + string.Join("\n", missing));
        }

        [Test]
        public void EveryManifestState_IsBoundWithItsFullFrameList()
        {
            var problems = new List<string>();
            foreach ((string file, PlayerEntry entry) in ManifestEntries())
            {
                PlayerDefinition def = Load(entry.playerKey);
                if (def?.assetConfig == null) continue;

                foreach (SheetEntry state in entry.states)
                {
                    List<Sprite> bound = SheetsFor(def.assetConfig, state.Name);
                    if (bound == null)
                    {
                        problems.Add($"{entry.playerKey}.{state.Name}: manifest names a state " +
                                     "EntityAssetConfig has no slot for");
                        continue;
                    }
                    if (bound.Count != state.sprites.Count)
                    {
                        problems.Add($"{entry.playerKey}.{state.Name}: bound {bound.Count} " +
                                     $"frames, manifest has {state.sprites.Count}");
                    }
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems) + RunTheImporter);
        }

        [Test]
        public void EveryManifestVariant_ExistsOnTheDefinition()
        {
            var problems = new List<string>();
            foreach ((string file, PlayerEntry entry) in ManifestEntries())
            {
                PlayerDefinition def = Load(entry.playerKey);
                if (def?.assetConfig == null) continue;

                foreach (SheetEntry variant in entry.attackVariants)
                {
                    AttackVariant bound = def.assetConfig.attackVariants?
                        .Find(v => v != null && string.Equals(v.key, variant.Name,
                                                              StringComparison.OrdinalIgnoreCase));
                    if (bound == null)
                        problems.Add($"{entry.playerKey}: attack variant '{variant.Name}' is missing");
                    else if (bound.sheets == null || bound.sheets.Count != variant.sprites.Count)
                        problems.Add($"{entry.playerKey}.attack:{variant.Name}: bound " +
                                     $"{bound.sheets?.Count ?? 0} frames, manifest has {variant.sprites.Count}");
                }

                foreach (SheetEntry variant in entry.castVariants)
                {
                    CastVariant bound = def.assetConfig.castVariants?
                        .Find(v => v != null && string.Equals(v.key, variant.Name,
                                                              StringComparison.OrdinalIgnoreCase));
                    if (bound == null)
                        problems.Add($"{entry.playerKey}: cast variant '{variant.Name}' is missing");
                    else if (bound.sheets == null || bound.sheets.Count != variant.sprites.Count)
                        problems.Add($"{entry.playerKey}.cast:{variant.Name}: bound " +
                                     $"{bound.sheets?.Count ?? 0} frames, manifest has {variant.sprites.Count}");
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems) + RunTheImporter);
        }

        [Test]
        public void EveryManifestLoadout_ExistsWithItsStates()
        {
            var problems = new List<string>();
            foreach ((string file, PlayerEntry entry) in ManifestEntries())
            {
                PlayerDefinition def = Load(entry.playerKey);
                if (def?.assetConfig == null) continue;

                foreach (LoadoutEntry loadout in entry.loadouts)
                {
                    if (loadout == null || string.IsNullOrEmpty(loadout.key)) continue;

                    Loadout bound = def.assetConfig.FindLoadout(loadout.key);
                    if (bound == null)
                    {
                        problems.Add($"{entry.playerKey}: loadout '{loadout.key}' is missing. " +
                                     "EntitySetup only attaches PlayerLoadoutController to a " +
                                     "character that declares one, so the toggle spell finds " +
                                     "nothing to toggle");
                        continue;
                    }

                    foreach (SheetEntry state in loadout.states)
                    {
                        LoadoutStateSheets over = bound.Find(state.Name);
                        if (over == null)
                            problems.Add($"{entry.playerKey}.{loadout.key}: no override for '{state.Name}'");
                        else if (over.sheets == null || over.sheets.Count != state.sprites.Count)
                            problems.Add($"{entry.playerKey}.{loadout.key}.{state.Name}: bound " +
                                         $"{over.sheets?.Count ?? 0} frames, manifest has {state.sprites.Count}");
                    }
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems) + RunTheImporter);
        }

        [Test]
        public void EveryManifestReservation_ReachedTheDefinition()
        {
            var problems = new List<string>();
            foreach ((string file, PlayerEntry entry) in ManifestEntries())
            {
                PlayerDefinition def = Load(entry.playerKey);
                if (def?.assetConfig == null) continue;

                foreach (SheetEntry variant in entry.attackVariants)
                {
                    if (variant.spellKeys == null || variant.spellKeys.Count == 0) continue;
                    AttackVariant bound = def.assetConfig.attackVariants?
                        .Find(v => v != null && string.Equals(v.key, variant.Name,
                                                              StringComparison.OrdinalIgnoreCase));
                    if (bound != null && !bound.IsReservedForSpell)
                        problems.Add($"{entry.playerKey}.attack:{variant.Name} should be reserved " +
                                     $"for [{string.Join(", ", variant.spellKeys)}] and is not");
                }

                foreach (SheetEntry variant in entry.castVariants)
                {
                    if (variant.spellKeys == null || variant.spellKeys.Count == 0) continue;
                    CastVariant bound = def.assetConfig.castVariants?
                        .Find(v => v != null && string.Equals(v.key, variant.Name,
                                                              StringComparison.OrdinalIgnoreCase));
                    if (bound != null && !bound.IsReservedForSpell)
                        problems.Add($"{entry.playerKey}.cast:{variant.Name} should be reserved " +
                                     $"for [{string.Join(", ", variant.spellKeys)}] and is not");
                }
            }

            // Only checks that SOMETHING is reserved, not that it matches: the manifest value
            // is a creation default and a designer is free to repoint it in the Inspector.
            // What must never happen is the reservation vanishing entirely, which is what a
            // rebuild that forgot to carry authored values across looks like.
            Assert.IsEmpty(problems, string.Join("\n", problems) + RunTheImporter);
        }
    }
}
