#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Players
{
    /// <summary>
    /// Binds the animation frames <c>tools/atlas/wave3/build_player_frames.py</c> slices,
    /// aligns and mirrors out of the staged side-view sheets into the
    /// <see cref="EntityAssetConfig"/> of an existing <see cref="PlayerDefinition"/>.
    ///
    /// Deliberately shaped like <see cref="Valkur.Editor.Monsters.MonsterFramesImporter"/>:
    /// the Python side owns the pixels and the per-sprite geometry, this side owns the
    /// ScriptableObjects, and the contract between them is every manifest matching
    /// <see cref="MANIFEST_SEARCH_PATTERN"/> under <see cref="MANIFEST_DIR_RELATIVE"/> —
    /// repo-relative and versioned in git, because the source sheets under
    /// <c>staging/</c> at the repo root are not (that folder is gitignored, and lives outside
    /// <c>Assets/</c> so Unity never imports 250 MB of art nothing references).
    ///
    /// Two directions, not eight
    /// -------------------------
    /// The staged art is drawn facing WEST, one direction only. Rather than teach
    /// <c>DirectionalAnimator</c> a two-direction mode — it never flips a sprite, and
    /// <c>ChaseState</c> says so in as many words because flipping corrupts a genuinely
    /// directional sheet — the Python step bakes the mirrored copy as its own sprite and
    /// this importer fills all eight buckets from the two. Each state's sprite list is
    /// therefore <c>framesPerDirection * 8</c> long and holds each sprite FOUR OR FIVE
    /// TIMES over: south/southEast/east/northEast/north take the MIRRORED (<c>_e</c>)
    /// frames, northWest/west/southWest the authored (<c>_w</c>) ones. That repetition is
    /// the point, not an accident — it is how <c>knight_red</c> already ships, and which
    /// half is the mirror is measured rather than assumed: see
    /// <c>PlayerTwoDirectionRigTests</c> and the pipeline section of CLAUDE.md.
    ///
    /// The import is IDEMPOTENT and keyed on <see cref="PlayerDefinition.playerKey"/>. Unlike
    /// the monster importer it NEVER CREATES a definition: a player class is a design entity
    /// with attributes, combat stats and a slot in the character-select flow, none of which an
    /// art import can invent. A manifest naming a key that no definition claims is reported
    /// and skipped.
    ///
    /// What this importer does NOT touch: attributes, combat stats, <c>basicSpeed</c>,
    /// <c>dashCharges</c>, <c>scaleConfig</c>, or the per-variant combat multipliers on an
    /// <see cref="AttackVariant"/> that already exists. Re-running it after re-slicing
    /// refreshes frames and nothing else — the same line the monster and building importers
    /// draw between pixel-pipeline output and design decisions.
    ///
    /// Neither menu item opens a dialog: both are driven from the MCP bridge as often as from
    /// the menu bar, and a modal dialog there hangs the calling tool.
    /// </summary>
    public static class PlayerFramesImporter
    {
        private const string MENU_DRY_RUN = "Valkur/Players/Import Frame Sheets (Dry Run)";
        private const string MENU_APPLY   = "Valkur/Players/Import Frame Sheets (Apply)";

        private const string MANIFEST_DIR_RELATIVE = "../../../tools/atlas/generated";
        private const string MANIFEST_SEARCH_PATTERN = "player_frames_manifest*.json";
        private const string PLAYER_CATALOG_ROOT = "Assets/_Project/Data/Catalogs/Players";
        private const string LOG_PREFIX = "[PlayerFramesImporter]";

        /// <summary>The states <see cref="EntityAssetConfig"/> has sheet slots for.</summary>
        private static readonly string[] KNOWN_STATES =
            { "idle", "walk", "chase", "cast", "attack", "damage", "death", "recover" };

        // ── Manifest schema (JsonUtility needs concrete serializable types, no Dictionary) ──

        [Serializable]
        private class Manifest
        {
            public string generator;
            public string generatedFrom;
            public int targetBodyPx;
            public List<PlayerEntry> players = new List<PlayerEntry>();
        }

        [Serializable]
        private class PlayerEntry
        {
            public string playerKey;
            public List<StateSheetEntry> states = new List<StateSheetEntry>();
            public List<StateSheetEntry> attackVariants = new List<StateSheetEntry>();
            public List<StateSheetEntry> castVariants = new List<StateSheetEntry>();
            public List<StateVariantGroupEntry> stateVariants = new List<StateVariantGroupEntry>();
            public List<LoadoutEntry> loadouts = new List<LoadoutEntry>();
        }

        /// <summary>One alternative look, overriding only the states it has art for.</summary>
        [Serializable]
        private class LoadoutEntry
        {
            public string key;
            public List<StateSheetEntry> states = new List<StateSheetEntry>();

            /// <summary>The alternatives for the states this loadout overrides. Grouped
            /// beside <see cref="states"/> rather than nested inside each entry because
            /// Unity's JsonUtility does not serialize a type that contains itself, and a
            /// <see cref="StateSheetEntry"/> holding a list of its own kind is exactly
            /// that.</summary>
            public List<StateVariantGroupEntry> stateVariants = new List<StateVariantGroupEntry>();

            /// <summary>The swings this loadout uses instead of the base attackVariants.
            /// Empty means the base rotation stands.</summary>
            public List<StateSheetEntry> attackVariants = new List<StateSheetEntry>();

            /// <summary>The casting animations this loadout uses instead of the base
            /// castVariants. Empty means the base rotation stands.</summary>
            public List<StateSheetEntry> castVariants = new List<StateSheetEntry>();
        }

        /// <summary>The alternatives for one state — a second walk cycle, a third death.</summary>
        [Serializable]
        private class StateVariantGroupEntry
        {
            public string state;
            public List<StateSheetEntry> variants = new List<StateSheetEntry>();
        }

        [Serializable]
        private class StateSheetEntry
        {
            /// <summary>One of <see cref="KNOWN_STATES"/> for a state; the variant key otherwise.</summary>
            public string state;
            public string key;
            public int framesPerDirection;
            /// <summary>framesPerDirection * 8, in S, SE, E, NE, N, NW, W, SW order.</summary>
            public List<string> sprites = new List<string>();

            /// <summary>Spells this variant is reserved for. A CREATION DEFAULT only —
            /// see <see cref="DefaultSpellKeys"/>.</summary>
            public List<string> spellKeys = new List<string>();

            /// <summary>Playback speed for this variant; 1 = the entity's normal rate.
            /// A creation default, like <see cref="spellKeys"/>.</summary>
            public float animationSpeedMultiplier = 1f;

            /// <summary>Whether the last frame holds instead of the animation looping.
            /// A creation default, like <see cref="spellKeys"/>.</summary>
            public bool holdLastFrame;

            public string Name => string.IsNullOrEmpty(state) ? key : state;
        }

        // ── Menu entry points ─────────────────────────────────────────────────────────

        [MenuItem(MENU_DRY_RUN)]
        public static void DryRun() => RunProduction(apply: false);

        [MenuItem(MENU_APPLY)]
        public static void Apply() => RunProduction(apply: true);

        /// <summary>Result of one import pass, returned so tests can assert on it directly
        /// instead of scraping console output.</summary>
        public sealed class ImportSummary
        {
            public bool Aborted;
            public readonly List<string> Updated = new List<string>();
            public readonly List<string> ClearedStates = new List<string>();
            public readonly List<string> UnknownKeys = new List<string>();
            public readonly List<string> MissingSprites = new List<string>();
            public readonly List<string> Sources = new List<string>();
        }

        private static void RunProduction(bool apply)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_DIR_RELATIVE));
            ImportSummary summary = Import(dir, PLAYER_CATALOG_ROOT, apply, refreshAssetDatabase: apply);
            if (!summary.Aborted)
                Report(apply, summary);
        }

        /// <summary>
        /// Reads every <c>player_frames_manifest*.json</c> under <paramref name="manifestDir"/>,
        /// validates them, and (when <paramref name="apply"/>) refreshes the sprite slots of the
        /// <see cref="PlayerDefinition"/> each entry names under <paramref name="playerCatalogRoot"/>.
        ///
        /// Exposed as the seam tests use: production runs it against the real manifest folder and
        /// the shipped catalog; a test runs it against a scratch manifest directory and a scratch
        /// catalog folder, so nothing shipped is ever at risk. Discovery, validation and binding
        /// are identical either way.
        /// </summary>
        public static ImportSummary Import(string manifestDir, string playerCatalogRoot,
                                           bool apply, bool refreshAssetDatabase = true)
        {
            var summary = new ImportSummary();

            if (!TryLoadManifests(manifestDir, out Manifest manifest, out List<string> sources))
            {
                summary.Aborted = true;
                return summary;
            }
            summary.Sources.AddRange(sources);

            if (!ValidateManifest(manifest, out int rejected))
            {
                Debug.LogError($"{LOG_PREFIX} Aborting: {rejected} manifest entries are invalid (see above).");
                summary.Aborted = true;
                return summary;
            }

            // Newly-written PNGs on disk are invisible to AssetDatabase until a refresh — the
            // Python step runs outside the Editor, so nothing else triggers one here.
            if (apply && refreshAssetDatabase)
                AssetDatabase.Refresh();

            Dictionary<string, PlayerDefinition> byKey = IndexExistingDefinitions(playerCatalogRoot);

            foreach (PlayerEntry entry in manifest.players)
            {
                if (!byKey.TryGetValue(entry.playerKey, out PlayerDefinition def))
                {
                    // Never created here — see the class doc. A missing class is an authoring
                    // gap for a human to close, not something an art import should invent.
                    summary.UnknownKeys.Add(entry.playerKey);
                    continue;
                }

                // Resolved regardless of apply, so a dry run reports missing sprites too.
                var resolvedStates = ResolveSheets(entry.states, entry.playerKey, summary.MissingSprites);
                var resolvedVariants = ResolveSheets(entry.attackVariants, entry.playerKey, summary.MissingSprites);
                var resolvedCasts = ResolveSheets(entry.castVariants, entry.playerKey, summary.MissingSprites);

                // Resolved as a list of lists so a loadout keeps its own state names: they are
                // the BASE state names (idle/walk/chase/…), so flattening them into one list
                // would collide with the base states resolved above.
                var resolvedLoadouts = new List<ResolvedLoadout>();
                if (entry.loadouts != null)
                {
                    foreach (LoadoutEntry loadout in entry.loadouts)
                    {
                        if (loadout == null || string.IsNullOrEmpty(loadout.key)) continue;
                        resolvedLoadouts.Add(new ResolvedLoadout
                        {
                            Key = loadout.key,
                            Source = loadout,
                            States = ResolveSheets(loadout.states, entry.playerKey, summary.MissingSprites),
                            StateVariants = ResolveVariantGroups(loadout.stateVariants, entry.playerKey, summary),
                            Attacks = ResolveSheets(loadout.attackVariants, entry.playerKey, summary.MissingSprites),
                            Casts = ResolveSheets(loadout.castVariants, entry.playerKey, summary.MissingSprites),
                        });
                    }
                }

                var resolvedStateVariants =
                    ResolveVariantGroups(entry.stateVariants, entry.playerKey, summary);

                summary.Updated.Add(entry.playerKey);
                if (!apply) continue;

                // Deliberately NOT Undo.RecordObject — a bulk import lands every touched asset
                // on the GLOBAL editor undo stack, and the first thing that pops it reverts them
                // all IN MEMORY to their pre-import state while the correct data sits on disk.
                // That is the BuildingPropImporter incident; SetDirty alone is the right tool for
                // data an operator re-runs rather than undoes.
                if (def.assetConfig == null)
                    def.assetConfig = new EntityAssetConfig();

                // Explicit rather than Auto: the Auto heuristic infers the layout from the frame
                // COUNT, and while every count this pipeline produces (8 * framesPerDirection)
                // happens to fall outside its 4-directional window, leaning on that coincidence
                // would make a future 12-, 16- or 20-frame state silently bind as 4-directional.
                def.assetConfig.directionLayout = EntitySheetDirectionLayout.EightDirectional;

                foreach ((string state, List<Sprite> frames) in resolvedStates)
                    AssignStateSheet(def.assetConfig, state, frames);

                ClearUnlistedStates(def.assetConfig, resolvedStates, entry.playerKey, summary);
                ApplyAttackVariants(def.assetConfig, resolvedVariants,
                                    DefaultSpellKeys(entry.attackVariants),
                                    DefaultPacing(entry.attackVariants));
                ApplyCastVariants(def.assetConfig, resolvedCasts,
                                  DefaultSpellKeys(entry.castVariants),
                                  DefaultPacing(entry.castVariants));
                ApplyStateVariants(def.assetConfig, resolvedStateVariants);
                ApplyLoadouts(def.assetConfig, resolvedLoadouts);

                EditorUtility.SetDirty(def);
            }

            if (apply && refreshAssetDatabase)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return summary;
        }

        // ── Manifest loading & validation ─────────────────────────────────────────────

        private static bool TryLoadManifests(string manifestDir, out Manifest merged, out List<string> sources)
        {
            merged = new Manifest();
            sources = new List<string>();

            if (!Directory.Exists(manifestDir))
            {
                Debug.LogError($"{LOG_PREFIX} Manifest directory not found: {manifestDir}");
                return false;
            }

            string[] files = Directory.GetFiles(manifestDir, MANIFEST_SEARCH_PATTERN);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                Debug.LogError($"{LOG_PREFIX} No {MANIFEST_SEARCH_PATTERN} found under {manifestDir}. " +
                               "Run tools/atlas/wave3/build_player_frames.py first.");
                return false;
            }

            foreach (string file in files)
            {
                Manifest one;
                try
                {
                    one = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} Could not parse '{file}': {ex.Message}");
                    return false;
                }

                if (one?.players == null)
                {
                    Debug.LogError($"{LOG_PREFIX} '{file}' carries no players array.");
                    return false;
                }

                sources.Add(Path.GetFileName(file));
                merged.players.AddRange(one.players);
            }

            return true;
        }

        private static bool ValidateManifest(Manifest manifest, out int rejected)
        {
            rejected = 0;
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (PlayerEntry entry in manifest.players)
            {
                if (string.IsNullOrWhiteSpace(entry.playerKey))
                {
                    Debug.LogError($"{LOG_PREFIX} A manifest entry has no playerKey.");
                    rejected++;
                    continue;
                }

                if (!seenKeys.Add(entry.playerKey))
                {
                    // Two waves both claiming one player would silently leave whichever ran
                    // last in charge of every slot, including the ones the other wave owned.
                    Debug.LogError($"{LOG_PREFIX} playerKey '{entry.playerKey}' appears in more " +
                                   "than one manifest entry.");
                    rejected++;
                    continue;
                }

                rejected += CountInvalidSheets(entry.states, entry.playerKey, requireKnownState: true);
                rejected += CountInvalidSheets(entry.attackVariants, entry.playerKey, requireKnownState: false);
                rejected += CountInvalidSheets(entry.castVariants, entry.playerKey, requireKnownState: false);
                rejected += CountInvalidVariantGroups(entry.stateVariants, entry.playerKey);
                if (entry.loadouts != null)
                {
                    foreach (LoadoutEntry loadout in entry.loadouts)
                    {
                        if (loadout == null) continue;
                        rejected += CountInvalidVariantGroups(loadout.stateVariants, entry.playerKey);
                        rejected += CountInvalidSheets(loadout.attackVariants, entry.playerKey, requireKnownState: false);
                        rejected += CountInvalidSheets(loadout.castVariants, entry.playerKey, requireKnownState: false);
                    }
                }
            }

            return rejected == 0;
        }

        /// <summary>
        /// Validates the per-state variant groups.
        ///
        /// The group's own name MUST be a known state — a typo there is silent otherwise:
        /// <c>EntityAssetConfig.FindStateVariants</c> would simply never match it and the
        /// alternatives would sit in the asset, round-trip, and never play. Attack and cast
        /// are refused by name for the same reason the binder refuses them: those two have
        /// their own lists with their own selection rules, and accepting them here would
        /// mean whichever install ran last wins.
        /// </summary>
        private static int CountInvalidVariantGroups(List<StateVariantGroupEntry> groups, string playerKey)
        {
            int rejected = 0;
            if (groups == null) return 0;

            foreach (StateVariantGroupEntry group in groups)
            {
                if (group == null) continue;
                string name = (group.state ?? string.Empty).Trim().ToLowerInvariant();
                if (Array.IndexOf(KNOWN_STATES, name) < 0)
                {
                    Debug.LogError($"{LOG_PREFIX} {playerKey}: stateVariants names unknown " +
                                   $"state '{group.state}'.");
                    rejected++;
                }
                else if (name == "attack" || name == "cast")
                {
                    Debug.LogError($"{LOG_PREFIX} {playerKey}: stateVariants may not name " +
                                   $"'{name}' — use attackVariants / castVariants.");
                    rejected++;
                }

                rejected += CountInvalidSheets(group.variants, playerKey, requireKnownState: false);
            }

            return rejected;
        }

        private static int CountInvalidSheets(List<StateSheetEntry> sheets, string playerKey, bool requireKnownState)
        {
            int rejected = 0;
            if (sheets == null) return 0;

            foreach (StateSheetEntry sheet in sheets)
            {
                string name = sheet.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    Debug.LogError($"{LOG_PREFIX} {playerKey}: a sheet entry has neither state nor key.");
                    rejected++;
                    continue;
                }

                if (requireKnownState && Array.IndexOf(KNOWN_STATES, name) < 0)
                {
                    Debug.LogError($"{LOG_PREFIX} {playerKey}: '{name}' is not one of the seven states " +
                                   $"EntityAssetConfig has a slot for ({string.Join(", ", KNOWN_STATES)}).");
                    rejected++;
                    continue;
                }

                int expected = sheet.framesPerDirection * 8;
                int actual = sheet.sprites?.Count ?? 0;
                if (sheet.framesPerDirection <= 0 || actual != expected)
                {
                    // The whole two-direction scheme rests on this: BuildEightDirectionalSet
                    // slices the list into eight CONTIGUOUS buckets of count/8, so a list that
                    // is not a clean multiple of eight silently redistributes every frame
                    // across the wrong directions rather than failing.
                    Debug.LogError($"{LOG_PREFIX} {playerKey}.{name}: expected " +
                                   $"{expected} sprites (framesPerDirection {sheet.framesPerDirection} x 8 " +
                                   $"directions) but the manifest lists {actual}.");
                    rejected++;
                }
            }

            return rejected;
        }

        // ── Binding ───────────────────────────────────────────────────────────────────

        private static Dictionary<string, PlayerDefinition> IndexExistingDefinitions(string playerCatalogRoot)
        {
            var byKey = new Dictionary<string, PlayerDefinition>(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:PlayerDefinition", new[] { playerCatalogRoot });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(path);
                if (def == null) continue;

                string key = string.IsNullOrWhiteSpace(def.playerKey)
                    ? def.name.Trim().ToLowerInvariant()
                    : def.playerKey.Trim().ToLowerInvariant();

                if (byKey.ContainsKey(key))
                {
                    Debug.LogWarning($"{LOG_PREFIX} playerKey '{key}' is claimed by more than one " +
                                     "PlayerDefinition asset. Updating the first one found; the " +
                                     "others keep stale data.");
                    continue;
                }
                byKey[key] = def;
            }

            return byKey;
        }

        /// <summary>Resolves every listed path to a <see cref="Sprite"/>, reporting (not throwing
        /// on) any that fails to load — a dry run needs to see these too.</summary>
        private static List<(string name, List<Sprite> frames)> ResolveSheets(
            List<StateSheetEntry> sheets, string playerKey, List<string> missingSprites)
        {
            var result = new List<(string, List<Sprite>)>();
            if (sheets == null) return result;

            foreach (StateSheetEntry sheet in sheets)
            {
                var frames = new List<Sprite>(sheet.sprites?.Count ?? 0);
                if (sheet.sprites != null)
                {
                    foreach (string path in sheet.sprites)
                    {
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite == null)
                        {
                            missingSprites.Add($"{playerKey} {sheet.Name}: {path}");
                            continue;
                        }
                        frames.Add(sprite);
                    }
                }
                result.Add((sheet.Name, frames));
            }
            return result;
        }

        /// <summary>
        /// Empties every state slot this player's manifest entry did NOT name.
        ///
        /// This is where the player importer deliberately parts company with
        /// <see cref="Valkur.Editor.Monsters.MonsterFramesImporter"/>, which leaves an unnamed
        /// slot exactly as it found it. That is right for a monster, where a manifest is often a
        /// partial refresh of an asset a designer also authors by hand. It is wrong for a player,
        /// because a wave REPLACES the character: the barbarian's manifest supplies an axe-wielding
        /// idle, walk, chase and attack, and the slots it does not supply were still holding the
        /// previous 8-direction art of a completely different character. The result renders as
        /// the barbarian swapping bodies the moment he casts or dies.
        ///
        /// Emptying is not the same as leaving a hole: <c>EntityAnimationBinder</c> falls an empty
        /// slot back to a neighbour (cast to walk, damage and death to idle), so what the player
        /// sees is the right character in a less specific pose, instead of the wrong character.
        /// </summary>
        private static void ClearUnlistedStates(EntityAssetConfig config,
                                                List<(string name, List<Sprite> frames)> resolved,
                                                string playerKey, ImportSummary summary)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string name, List<Sprite> frames) in resolved)
            {
                if (frames != null && frames.Count > 0)
                    listed.Add(name);
            }

            foreach (string state in KNOWN_STATES)
            {
                if (listed.Contains(state))
                    continue;

                int had = CountStateSheet(config, state);
                if (had == 0)
                    continue;

                AssignStateSheet(config, state, new List<Sprite>(), allowEmpty: true);
                summary.ClearedStates.Add($"{playerKey}.{state} ({had} frames)");
            }
        }

        private static int CountStateSheet(EntityAssetConfig config, string state)
        {
            List<Sprite> sheet = state switch
            {
                "idle" => config.idleSheets,
                "walk" => config.walkSheets,
                "chase" => config.chaseSheets,
                "cast" => config.castSheets,
                "attack" => config.attackSheets,
                "damage" => config.damageSheets,
                "death" => config.deathSheets,
                "recover" => config.recoverSheets,
                _ => null,
            };
            return sheet?.Count ?? 0;
        }

        private static void AssignStateSheet(EntityAssetConfig config, string state, List<Sprite> frames,
                                             bool allowEmpty = false)
        {
            // An empty list from the RESOLVE path means every sprite failed to load. Writing it
            // would blank a slot that currently holds working art, so leave the slot alone and let
            // the missing-sprite report be the signal — a half-written config fails quietly where
            // a skipped one is loud. ClearUnlistedStates passes allowEmpty because there the
            // emptiness is the intent, not a failure.
            if (!allowEmpty && (frames == null || frames.Count == 0))
                return;

            switch (state)
            {
                case "idle":   config.idleSheets = frames; break;
                case "walk":   config.walkSheets = frames; break;
                case "chase":  config.chaseSheets = frames; break;
                case "cast":   config.castSheets = frames; break;
                case "attack": config.attackSheets = frames; break;
                case "damage": config.damageSheets = frames; break;
                case "death":  config.deathSheets = frames; break;
                case "recover": config.recoverSheets = frames; break;
                default:
                    Debug.LogWarning($"{LOG_PREFIX} Ignoring unknown state '{state}'.");
                    break;
            }
        }

        /// <summary>
        /// Refreshes each variant's frames, keeping the combat data a designer authored.
        ///
        /// The order matters and the manifest owns it: <c>attackVariants[0]</c> is what a picker
        /// falls back to, so the manifest lists the default swing first. A variant the manifest
        /// no longer names is DROPPED — leaving it would keep pointing at sprites from a previous
        /// wave that the re-slice may have renamed out from under it.
        /// </summary>
        private static void ApplyAttackVariants(EntityAssetConfig config,
                                                List<(string name, List<Sprite> frames)> resolved,
                                                Dictionary<string, List<string>> defaultSpellKeys = null,
                                                Dictionary<string, (float speed, bool hold)> defaultPacing = null)
        {
            var rebuilt = BuildAttackVariantList(config.attackVariants, resolved,
                                                 defaultSpellKeys, defaultPacing);
            if (rebuilt != null) config.attackVariants = rebuilt;
        }

        /// <summary>
        /// The rebuild itself, over a list rather than over the config, so a LOADOUT's own
        /// swings go through exactly this path. A second implementation is how the base
        /// rotation and a loadout's would end up disagreeing about whether an authored
        /// reservation survives a re-import.
        /// </summary>
        private static List<AttackVariant> BuildAttackVariantList(
            List<AttackVariant> previous,
            List<(string name, List<Sprite> frames)> resolved,
            Dictionary<string, List<string>> defaultSpellKeys = null,
            Dictionary<string, (float speed, bool hold)> defaultPacing = null)
        {
            if (resolved == null || resolved.Count == 0)
                return null;

            var existing = new Dictionary<string, AttackVariant>(StringComparer.Ordinal);
            if (previous != null)
            {
                foreach (AttackVariant variant in previous)
                {
                    if (variant != null && !string.IsNullOrEmpty(variant.key))
                        existing[variant.key] = variant;
                }
            }

            var rebuilt = new List<AttackVariant>(resolved.Count);
            foreach ((string name, List<Sprite> frames) in resolved)
            {
                if (frames == null || frames.Count == 0)
                    continue;

                if (!existing.TryGetValue(name, out AttackVariant variant))
                {
                    variant = new AttackVariant { key = name };
                    // Only on creation — an authored reservation is never overwritten.
                    if (defaultSpellKeys != null && defaultSpellKeys.TryGetValue(name, out var seed))
                        variant.spellKeys = new List<string>(seed);
                    if (defaultPacing != null && defaultPacing.TryGetValue(name, out var pace))
                    {
                        variant.animationSpeedMultiplier = pace.speed;
                        variant.holdLastFrame = pace.hold;
                    }
                }
                else if (!variant.IsReservedForSpell &&
                         defaultSpellKeys != null && defaultSpellKeys.TryGetValue(name, out var seedExisting))
                {
                    // A variant that predates the reservation field carries an empty list,
                    // which is indistinguishable from "deliberately unreserved". Seeding it is
                    // the lesser wrong: the manifest is the only place that opinion is written
                    // down, and leaving it empty ships the animation unreachable.
                    variant.spellKeys = new List<string>(seedExisting);
                }

                // Frames are this importer's business; damage/range/cooldown/weight and the
                // distance gates are a design decision per move and are left exactly as found.
                variant.sheets = frames;
                rebuilt.Add(variant);
            }

            return rebuilt;
        }

        /// <summary>
        /// Same for the casting animations. A <see cref="CastVariant"/> carries no combat
        /// data — a spell's damage is on its <c>SpellDefinition</c> — but it does carry
        /// <c>spellKeys</c>, the reservation that pins one spell to one animation, and that
        /// is a design decision an art re-import has no business discarding. It is carried
        /// across BY KEY rather than by position, because the manifest decides both the order
        /// and the membership of this list and a wave that adds a sixth spellcast would
        /// otherwise slide every reservation onto its neighbour.
        /// </summary>
        private static void ApplyCastVariants(EntityAssetConfig config,
                                              List<(string name, List<Sprite> frames)> resolved,
                                              Dictionary<string, List<string>> defaultSpellKeys = null,
                                              Dictionary<string, (float speed, bool hold)> defaultPacing = null)
        {
            var rebuilt = BuildCastVariantList(config.castVariants, resolved,
                                               defaultSpellKeys, defaultPacing);
            if (rebuilt != null) config.castVariants = rebuilt;
        }

        /// <summary>The rebuild itself, over a list — see <see cref="BuildAttackVariantList"/>
        /// for why a loadout shares this path rather than getting its own.</summary>
        private static List<CastVariant> BuildCastVariantList(
            List<CastVariant> previous,
            List<(string name, List<Sprite> frames)> resolved,
            Dictionary<string, List<string>> defaultSpellKeys = null,
            Dictionary<string, (float speed, bool hold)> defaultPacing = null)
        {
            if (resolved == null || resolved.Count == 0)
                return null;

            var authoredSpellKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            // Pacing is captured separately from the reservation because a variant may carry
            // one without the other, and "unreserved" must not also mean "reset to 1x".
            var authoredPacing = new Dictionary<string, (float speed, bool hold)>(StringComparer.OrdinalIgnoreCase);
            // The repeat stretch and the timeline are authored per animation on the asset and
            // the manifest has no opinion on either, so the previous variant is the only source.
            // Both used to be dropped here: a re-import rebuilt every CastVariant from scratch and
            // silently deleted every timeline an author had drawn.
            var previousByKey = new Dictionary<string, CastVariant>(StringComparer.OrdinalIgnoreCase);
            if (previous != null)
            {
                foreach (CastVariant existing in previous)
                {
                    if (existing == null || string.IsNullOrEmpty(existing.key)) continue;
                    previousByKey[existing.key] = existing;
                    if (existing.IsReservedForSpell)
                        authoredSpellKeys[existing.key] = new List<string>(existing.spellKeys);

                    float speed = existing.animationSpeedMultiplier > 0f
                        ? existing.animationSpeedMultiplier : 1f;
                    if (!Mathf.Approximately(speed, 1f) || existing.holdLastFrame)
                        authoredPacing[existing.key] = (speed, existing.holdLastFrame);
                }
            }

            var rebuilt = new List<CastVariant>(resolved.Count);
            foreach ((string name, List<Sprite> frames) in resolved)
            {
                if (frames == null || frames.Count == 0)
                    continue;
                var variant = new CastVariant { key = name, sheets = frames };
                // Authored wins; the manifest's declaration is only what a variant nobody has
                // pinned yet starts life with.
                if (authoredSpellKeys.TryGetValue(name, out var keys))
                    variant.spellKeys = keys;
                else if (defaultSpellKeys != null && defaultSpellKeys.TryGetValue(name, out var seed))
                    variant.spellKeys = new List<string>(seed);

                if (authoredPacing.TryGetValue(name, out var authored))
                {
                    variant.animationSpeedMultiplier = authored.speed;
                    variant.holdLastFrame = authored.hold;
                }
                else if (defaultPacing != null && defaultPacing.TryGetValue(name, out var pace))
                {
                    variant.animationSpeedMultiplier = pace.speed;
                    variant.holdLastFrame = pace.hold;
                }

                if (previousByKey.TryGetValue(name, out CastVariant before))
                {
                    variant.repeatFrom = before.repeatFrom;
                    variant.repeatFrameCount = before.repeatFrameCount;
                    if (before.timeline != null) variant.timeline = before.timeline;
                }
                rebuilt.Add(variant);
            }

            return rebuilt;
        }

        /// <summary>
        /// Rebuilds the alternative LOOKS. Like the two variant paths this replaces the list
        /// outright, and like them it is keyed by name rather than by position — a wave that
        /// adds a second loadout must not shift the first one's meaning.
        ///
        /// A loadout the manifest does not name is REMOVED, which is the same ownership rule
        /// <c>ClearUnlistedStates</c> applies to the base states and for the same reason: a
        /// player wave is a replacement, not a partial refresh, and a loadout whose art was
        /// dropped from the pipeline must not keep rendering from sprites nothing regenerates.
        /// </summary>
        /// <summary>One loadout with every sprite already looked up.</summary>
        private sealed class ResolvedLoadout
        {
            public string Key;
            public LoadoutEntry Source;
            public List<(string name, List<Sprite> frames)> States;
            public List<ResolvedVariantGroup> StateVariants;
            public List<(string name, List<Sprite> frames)> Attacks;
            public List<(string name, List<Sprite> frames)> Casts;
        }

        private static void ApplyLoadouts(EntityAssetConfig config, List<ResolvedLoadout> resolved)
        {
            if (resolved == null || resolved.Count == 0)
            {
                config.loadouts = new List<Loadout>();
                return;
            }

            var previousByKey = new Dictionary<string, Loadout>(StringComparer.OrdinalIgnoreCase);
            if (config.loadouts != null)
            {
                foreach (Loadout old in config.loadouts)
                {
                    if (old != null && !string.IsNullOrEmpty(old.key)) previousByKey[old.key] = old;
                }
            }

            var rebuilt = new List<Loadout>(resolved.Count);
            foreach (ResolvedLoadout src in resolved)
            {
                var loadout = new Loadout { key = src.Key, states = new List<LoadoutStateSheets>() };
                foreach ((string state, List<Sprite> frames) in src.States)
                {
                    if (frames == null || frames.Count == 0) continue;
                    loadout.states.Add(new LoadoutStateSheets
                    {
                        state = state,
                        sheets = frames,
                        variants = VariantsNamed(src.StateVariants, state),
                    });
                }

                // Carried across BY KEY from the loadout of the same name, so a designer's
                // reservation survives a re-import here exactly as it does at base level.
                previousByKey.TryGetValue(src.Key, out Loadout previous);
                loadout.attackVariants = BuildAttackVariantList(
                    previous?.attackVariants, src.Attacks,
                    DefaultSpellKeys(src.Source?.attackVariants),
                    DefaultPacing(src.Source?.attackVariants)) ?? new List<AttackVariant>();
                loadout.castVariants = BuildCastVariantList(
                    previous?.castVariants, src.Casts,
                    DefaultSpellKeys(src.Source?.castVariants),
                    DefaultPacing(src.Source?.castVariants)) ?? new List<CastVariant>();

                if (loadout.states.Count > 0)
                    rebuilt.Add(loadout);
            }

            config.loadouts = rebuilt;
        }

        /// <summary>One state's resolved alternatives, with the sprites already looked up.</summary>
        private sealed class ResolvedVariantGroup
        {
            public string State;
            public readonly List<StateVariant> Variants = new List<StateVariant>();
        }

        /// <summary>
        /// Turns the manifest's variant groups into ready <see cref="StateVariant"/> objects.
        ///
        /// Shared by the base config and by every loadout, because the two carry the same
        /// thing under the same rules and a second implementation is how the base walk and
        /// the loadout walk end up disagreeing about whether an empty group means "no
        /// variants" or "keep the previous ones".
        /// </summary>
        private static List<ResolvedVariantGroup> ResolveVariantGroups(
            List<StateVariantGroupEntry> groups, string playerKey, ImportSummary summary)
        {
            var resolved = new List<ResolvedVariantGroup>();
            if (groups == null) return resolved;

            foreach (StateVariantGroupEntry group in groups)
            {
                if (group == null || string.IsNullOrEmpty(group.state)) continue;

                var built = new ResolvedVariantGroup { State = group.state };
                foreach ((string name, List<Sprite> frames) in
                         ResolveSheets(group.variants, playerKey, summary.MissingSprites))
                {
                    if (frames == null || frames.Count == 0) continue;
                    StateSheetEntry authored = FindEntry(group.variants, name);
                    built.Variants.Add(new StateVariant
                    {
                        key = name,
                        sheets = frames,
                        // Straight from the manifest rather than as a creation default, unlike
                        // an attack or cast variant's pacing. There is nothing to preserve: a
                        // StateVariant carries no reservation and no combat multiplier, so the
                        // whole object is pipeline output and the importer owns all of it.
                        animationSpeedMultiplier = authored != null && authored.animationSpeedMultiplier > 0f
                            ? authored.animationSpeedMultiplier
                            : 1f,
                        holdLastFrame = authored != null && authored.holdLastFrame,
                    });
                }

                if (built.Variants.Count > 0) resolved.Add(built);
            }

            return resolved;
        }

        private static StateSheetEntry FindEntry(List<StateSheetEntry> entries, string name)
        {
            if (entries == null) return null;
            foreach (StateSheetEntry entry in entries)
            {
                if (entry != null &&
                    string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private static List<StateVariant> VariantsNamed(List<ResolvedVariantGroup> groups, string state)
        {
            var empty = new List<StateVariant>();
            if (groups == null) return empty;
            foreach (ResolvedVariantGroup group in groups)
            {
                if (string.Equals(group.State, state, StringComparison.OrdinalIgnoreCase))
                    return group.Variants;
            }
            return empty;
        }

        /// <summary>
        /// Rebuilds the base config's per-state alternatives. Replaces the list outright, the
        /// same ownership rule the two variant paths and <c>ApplyLoadouts</c> follow: a player
        /// wave is a replacement, so a group the manifest stopped naming must stop rotating.
        /// </summary>
        private static void ApplyStateVariants(EntityAssetConfig config,
                                               List<ResolvedVariantGroup> resolved)
        {
            var rebuilt = new List<StateVariantGroup>();
            if (resolved != null)
            {
                foreach (ResolvedVariantGroup group in resolved)
                {
                    rebuilt.Add(new StateVariantGroup
                    {
                        state = group.State,
                        variants = group.Variants,
                    });
                }
            }

            config.stateVariants = rebuilt;
        }

        /// <summary>
        /// The manifest's reservation defaults, by variant key.
        ///
        /// A DEFAULT, not an assignment: it is written when the importer CREATES a variant,
        /// and a reservation already on the asset wins over it. Both halves are needed. A
        /// brand-new animation that arrives unpinned ships as an unreachable rotation step
        /// until someone remembers to pin it in the Inspector, which is the second step that
        /// never happens; and a re-import that overwrote the authored value would undo every
        /// pin a designer has moved since. The same shape <c>TilesetRulesetImporter</c> uses
        /// for a ruleset's terrain names, for the same reason.
        /// </summary>
        /// <summary>The manifest's pacing defaults, by variant key. Same creation-default
        /// rule as <see cref="DefaultSpellKeys"/>: written when the variant is created,
        /// never over an authored value.</summary>
        private static Dictionary<string, (float speed, bool hold)> DefaultPacing(
            List<StateSheetEntry> entries)
        {
            var byKey = new Dictionary<string, (float, bool)>(StringComparer.OrdinalIgnoreCase);
            if (entries == null) return byKey;

            foreach (StateSheetEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                float speed = entry.animationSpeedMultiplier > 0f ? entry.animationSpeedMultiplier : 1f;
                // A neutral row is not worth recording — it is what a fresh variant already is,
                // and storing it would make "the manifest has an opinion" untestable.
                if (Mathf.Approximately(speed, 1f) && !entry.holdLastFrame) continue;
                byKey[entry.Name] = (speed, entry.holdLastFrame);
            }
            return byKey;
        }

        private static Dictionary<string, List<string>> DefaultSpellKeys(List<StateSheetEntry> entries)
        {
            var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (entries == null) return byKey;

            foreach (StateSheetEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.spellKeys == null || entry.spellKeys.Count == 0) continue;
                byKey[entry.Name] = new List<string>(entry.spellKeys);
            }
            return byKey;
        }

        // ── Reporting ─────────────────────────────────────────────────────────────────

        private static void Report(bool apply, ImportSummary summary)
        {
            string mode = apply ? "APPLY" : "DRY RUN";
            Debug.Log($"{LOG_PREFIX} {mode} — read {summary.Sources.Count} manifest(s): " +
                      $"{string.Join(", ", summary.Sources)}. " +
                      $"Updated {summary.Updated.Count} PlayerDefinition(s)" +
                      (summary.Updated.Count > 0 ? $": {string.Join(", ", summary.Updated)}" : "") + ".");

            foreach (string key in summary.UnknownKeys)
            {
                Debug.LogError($"{LOG_PREFIX} No PlayerDefinition claims playerKey '{key}'. This " +
                               "importer never creates one — a player class carries attributes, " +
                               "combat stats and a character-select slot that an art import " +
                               "cannot invent. Create the asset, then re-run.");
            }

            if (summary.ClearedStates.Count > 0)
            {
                // Loud on purpose: this is the importer throwing away art that was bound before
                // it ran. Silent would make a wave that forgot a state look like it shipped one.
                Debug.LogWarning($"{LOG_PREFIX} Cleared {summary.ClearedStates.Count} state slot(s) " +
                                 "no manifest claimed, so the character cannot mix two art sets. " +
                                 "EntityAnimationBinder falls each one back to a neighbour:\n  " +
                                 string.Join("\n  ", summary.ClearedStates));
            }

            if (summary.MissingSprites.Count > 0)
            {
                Debug.LogError($"{LOG_PREFIX} {summary.MissingSprites.Count} sprite(s) failed to load. " +
                               "The affected slots were left untouched. First 10:\n  " +
                               string.Join("\n  ", summary.MissingSprites.GetRange(
                                   0, Mathf.Min(10, summary.MissingSprites.Count))));
            }
        }
    }
}
#endif
