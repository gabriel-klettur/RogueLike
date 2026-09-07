#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Monsters
{
    /// <summary>
    /// Builds the DARK ROSTER: one hostile <see cref="MonsterDefinition"/> per playable class,
    /// wearing that class's own animation art tinted to a silhouette.
    ///
    /// <para>WHY GENERATE RATHER THAN AUTHOR SIX ASSETS BY HAND. The art is the whole point of
    /// these enemies and it is not theirs — it belongs to the player classes, and it moves.
    /// Every wave of <c>build_player_frames.py</c> plus
    /// <see cref="Valkur.Editor.Players.PlayerFramesImporter"/> rewrites a class's
    /// <see cref="EntityAssetConfig"/>: new attack variants, new cast variants, a loadout, a
    /// different frame count. A hand-copied monster asset is a snapshot of the art as it was
    /// on the afternoon somebody made it, and it drifts silently — the dark twin keeps swinging
    /// an animation the player class no longer has. Re-running this is how the two stay the
    /// same character.</para>
    ///
    /// <para>SO THE SPLIT IS: ART IS ALWAYS REWRITTEN, DESIGN IS WRITTEN ONCE. The generator
    /// owns <c>assetConfig</c> (every sprite, every variant, and the tint) and takes it back on
    /// every run. It owns stats, AI tuning and the spell list only on CREATION; after that they
    /// are an author's to tune in the Inspector or the Entities editor and a re-import never
    /// touches them. That is the same contract <c>TilesetRulesetImporter</c> and the persona
    /// importer use, drawn one field-group lower — and it is drawn HERE rather than left to
    /// taste because the failure is asymmetric: stale art is invisible until someone plays the
    /// enemy, while a silently reverted balance pass is invisible until someone plays the
    /// GAME.</para>
    ///
    /// <para>THE COPY GOES THROUGH <see cref="SerializedObject.CopyFromSerializedProperty"/>,
    /// not through a hand-written field-by-field clone. <see cref="EntityAssetConfig"/> has
    /// grown a recover slot, an attack-variant list, a cast-variant list, a state-pacing list,
    /// a sheet-layout enum and a loadout list since it was written, and every one of those
    /// arrived without anyone thinking about this file. A copier that enumerates fields is a
    /// second list of them that nothing forces anybody to update: it would keep compiling,
    /// keep running, and quietly stop carrying whatever was added last. The serialized copy
    /// carries the whole subtree by construction and cannot fall behind.</para>
    ///
    /// <para>NO <see cref="Undo"/>. A bulk asset generator that records to the global undo
    /// stack is how 193 building templates were reverted in memory to their empty creation
    /// state by an unrelated Ctrl+Z inside the test suite — see the CLAUDE.md gotcha. This is
    /// re-run, not undone.</para>
    /// </summary>
    public static class DarkRosterBuilder
    {
        private const string MENU_DRY_RUN = "Valkur/Monsters/Build Dark Roster (Dry Run)";
        private const string MENU_APPLY   = "Valkur/Monsters/Build Dark Roster";

        private const string PlayersDir  = "Assets/_Project/Data/Catalogs/Players";
        private const string OutputDir   = "Assets/_Project/Data/Catalogs/Monsters/Dark";
        private const string CatalogPath = "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";

        /// <summary>The FSM set every dark entity uses. Declared in StreamingAssets/FSM/sets.json.</summary>
        public const string DarkFsmSet = "Monster_Dark";

        /// <summary>Prefix on both the asset name and the monsterKey.</summary>
        public const string KeyPrefix = "dark_";

        /// <summary>
        /// The silhouette.
        ///
        /// <para>PURE BLACK, AND OPAQUE — the alpha is load-bearing rather than cosmetic.
        /// <c>EntityAnimationBinder</c> reads a tint of <c>(0,0,0,0)</c> as "nobody authored
        /// one" and substitutes white, so an alpha-zero black is not a very dark entity, it is
        /// an untinted one. Every field of this colour is doing work.</para>
        ///
        /// <para>What makes black SURVIVABLE as a design is that it now travels on
        /// <c>SpriteRenderer.color</c> rather than in the material's <c>_Color</c>: it becomes
        /// the <c>SpriteTintStack</c>'s base, so a hit flash lerps away from it and the player
        /// can still see their shots land. Routed the old way it would have multiplied every
        /// downstream effect to zero.</para>
        /// </summary>
        public static readonly Color DarkTint = new Color(0f, 0f, 0f, 1f);

        /// <summary>
        /// One row per playable class: what the dark twin of it is FOR.
        ///
        /// <para>They are not one enemy in six skins. Each takes the shape its source class
        /// already has — the dwarf's armour, the elven's speed, the mague's range — because
        /// the art is going to say so whatever the numbers do, and an armoured silhouette that
        /// dies in two hits reads as a bug rather than as a choice.</para>
        ///
        /// <para><c>DesiredRange</c> is what separates a caster from a melee monster wearing a
        /// robe: above zero, <c>ChaseState</c> holds a standoff band instead of closing all the
        /// way, which is also the only way the ranged spells below ever get to fire.
        /// <c>DodgeChance</c> is the second axis: the light classes buy their survivability by
        /// moving, the heavy ones by armour, so the same projectile is a different problem
        /// depending on who threw it.</para>
        /// </summary>
        private readonly struct DarkProfile
        {
            public readonly string PlayerKey;
            public readonly string DisplayName;
            public readonly int Hp;
            public readonly float Speed;
            public readonly float ChasingSpeed;
            public readonly int Defense;
            public readonly int Power;
            public readonly int MeleeDamage;
            public readonly float MeleeRange;
            public readonly float MeleeCooldown;
            public readonly float AggroRange;
            public readonly float AttackWindup;
            public readonly float DesiredRange;
            public readonly float DodgeChance;
            public readonly int XpReward;
            public readonly string[] Spells;

            public DarkProfile(string playerKey, string displayName, int hp, float speed,
                               float chasingSpeed, int defense, int power, int meleeDamage,
                               float meleeRange, float meleeCooldown, float aggroRange,
                               float attackWindup, float desiredRange, float dodgeChance,
                               int xpReward, string[] spells)
            {
                PlayerKey = playerKey; DisplayName = displayName; Hp = hp; Speed = speed;
                ChasingSpeed = chasingSpeed; Defense = defense; Power = power;
                MeleeDamage = meleeDamage; MeleeRange = meleeRange;
                MeleeCooldown = meleeCooldown; AggroRange = aggroRange;
                AttackWindup = attackWindup; DesiredRange = desiredRange;
                DodgeChance = dodgeChance; XpReward = xpReward; Spells = spells;
            }
        }

        // Speeds are on the MONSTER scale (knight_red walks 1.2 and chases 3), not the player's
        // basicSpeed scale (4-7). Deriving them from the PlayerDefinition would look like it
        // respected the class and would put a Dark Valkyrie on the field at more than twice the
        // fastest monster in the game.
        //
        // Every spell here is one an NPC can actually cast. The two that author spawnAtMouse
        // (shadow_step, leap_slam) degrade to facing-and-range for a caster that is not tagged
        // Player, which is SpellTargeting's documented behaviour and the right one — a monster
        // has no pointer. Four is the ceiling that matters: SpellCaster has four slots and
        // EntitySetup.ConfigureMonsterAutoCast wires only as many entries as there are slots.
        private static readonly DarkProfile[] Profiles =
        {
            new DarkProfile("dwarf", "Dark Dwarf",
                hp: 190, speed: 1.1f, chasingSpeed: 3.0f, defense: 14, power: 16,
                meleeDamage: 16, meleeRange: 2.0f, meleeCooldown: 1.1f, aggroRange: 9f,
                attackWindup: 0.45f, desiredRange: 0f, dodgeChance: 0.20f, xpReward: 90,
                spells: new[] { "hostile_slash_dark", "leap_slam", "thunderclap" }),

            new DarkProfile("barbarian", "Dark Barbarian",
                hp: 210, speed: 1.3f, chasingSpeed: 3.6f, defense: 8, power: 22,
                meleeDamage: 22, meleeRange: 2.2f, meleeCooldown: 1.3f, aggroRange: 10f,
                attackWindup: 0.5f, desiredRange: 0f, dodgeChance: 0.15f, xpReward: 110,
                spells: new[] { "hostile_slash_dark", "war_cry", "leap_slam" }),

            new DarkProfile("elven", "Dark Elven",
                hp: 130, speed: 1.6f, chasingSpeed: 4.4f, defense: 4, power: 14,
                meleeDamage: 13, meleeRange: 2.0f, meleeCooldown: 0.75f, aggroRange: 11f,
                attackWindup: 0.22f, desiredRange: 0f, dodgeChance: 0.55f, xpReward: 95,
                spells: new[] { "hostile_slash_dark", "seeking_shard", "shadow_step" }),

            new DarkProfile("mague", "Dark Mague",
                hp: 110, speed: 1.2f, chasingSpeed: 3.0f, defense: 3, power: 20,
                meleeDamage: 8, meleeRange: 1.8f, meleeCooldown: 1.4f, aggroRange: 13f,
                attackWindup: 0.3f, desiredRange: 6.5f, dodgeChance: 0.40f, xpReward: 105,
                spells: new[] { "darkball", "void_lance", "curse_of_frailty", "frost_nova" }),

            new DarkProfile("valkyrie", "Dark Valkyrie",
                hp: 165, speed: 1.5f, chasingSpeed: 4.2f, defense: 9, power: 18,
                meleeDamage: 17, meleeRange: 2.1f, meleeCooldown: 0.95f, aggroRange: 11f,
                attackWindup: 0.32f, desiredRange: 0f, dodgeChance: 0.35f, xpReward: 105,
                spells: new[] { "hostile_slash_dark", "thunderclap", "hostile_dash" }),

            // The one whose reach is not ~2: she is baked at 256 px / PPU 96 = 2.667 world
            // units against the dwarf's 1.797, so a swing tuned for the rest of the roster
            // would visibly stop short of her own arms.
            new DarkProfile("vampire", "Dark Vampire",
                hp: 240, speed: 1.4f, chasingSpeed: 4.0f, defense: 11, power: 24,
                meleeDamage: 20, meleeRange: 2.9f, meleeCooldown: 1.0f, aggroRange: 13f,
                attackWindup: 0.35f, desiredRange: 0f, dodgeChance: 0.45f, xpReward: 160,
                spells: new[] { "hostile_slash_dark", "void_lance", "curse_of_frailty", "shadow_step" }),
        };

        [MenuItem(MENU_DRY_RUN)]
        public static void DryRun() => Run(apply: false);

        [MenuItem(MENU_APPLY)]
        public static void Apply() => Run(apply: true);

        /// <summary>The monsterKey a given player class produces. Public so tests share it.</summary>
        public static string KeyFor(string playerKey) => KeyPrefix + playerKey;

        /// <summary>Every key this builder is responsible for. Public so tests share it.</summary>
        public static IEnumerable<string> AllKeys()
        {
            foreach (var p in Profiles) yield return KeyFor(p.PlayerKey);
        }

        private static void Run(bool apply)
        {
            var log = new StringBuilder();
            log.AppendLine(apply
                ? "[DarkRosterBuilder] APPLY"
                : "[DarkRosterBuilder] DRY RUN — nothing is written");

            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[DarkRosterBuilder] MonsterCatalog not found at {CatalogPath}. " +
                               "Nothing written.");
                return;
            }

            if (apply && !AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/_Project/Data/Catalogs/Monsters", "Dark");

            int created = 0, refreshed = 0, missing = 0;

            foreach (var profile in Profiles)
            {
                var player = LoadPlayer(profile.PlayerKey);
                if (player == null)
                {
                    log.AppendLine($"  MISSING  {profile.PlayerKey} — no PlayerDefinition under {PlayersDir}");
                    missing++;
                    continue;
                }

                string key = KeyFor(profile.PlayerKey);
                string path = $"{OutputDir}/{key}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                bool isNew = existing == null;

                if (!apply)
                {
                    log.AppendLine($"  {(isNew ? "CREATE " : "REFRESH")}  {key}  " +
                                   $"(art from {profile.PlayerKey}, " +
                                   $"{(isNew ? "seeding stats + spells" : "stats and spells left alone")})");
                    if (isNew) created++; else refreshed++;
                    continue;
                }

                var def = existing;
                if (isNew)
                {
                    def = ScriptableObject.CreateInstance<MonsterDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                    SeedDesign(def, profile);
                    created++;
                }
                else
                {
                    refreshed++;
                }

                CopyArt(player, def);

                // Identity is generator-owned in both cases: it is what the catalog, the FSM
                // assignment and every test key off, and an author renaming it silently
                // unhooks all three.
                def.monsterKey = key;
                def.displayName = profile.DisplayName;

                EditorUtility.SetDirty(def);
                catalog.UpsertDefinition(def);

                log.AppendLine($"  {(isNew ? "CREATED" : "REFRESHED")}  {key}");
            }

            if (apply)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            log.AppendLine($"  → {created} created, {refreshed} refreshed, {missing} missing.");
            Debug.Log(log.ToString());
        }

        private static PlayerDefinition LoadPlayer(string playerKey)
        {
            var direct = AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayersDir}/{playerKey}.asset");
            if (direct != null) return direct;

            // Fallback for a class whose asset file was renamed away from its key.
            foreach (var guid in AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayersDir }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.playerKey == playerKey) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Takes the whole <c>assetConfig</c> subtree across, then blackens it.
        ///
        /// <para>The tint is written through the SAME <see cref="SerializedObject"/> as the
        /// copy and after it, because the copy overwrites everything under that path — setting
        /// the colour first and copying second produces an asset that looks exactly right in
        /// code and ships untinted.</para>
        /// </summary>
        private static void CopyArt(PlayerDefinition from, MonsterDefinition to)
        {
            var src = new SerializedObject(from);
            var dst = new SerializedObject(to);

            var prop = src.FindProperty("assetConfig");
            if (prop == null)
            {
                Debug.LogError("[DarkRosterBuilder] PlayerDefinition has no 'assetConfig' property. " +
                               "The field was renamed; this builder is blind until it is updated.");
                return;
            }

            dst.CopyFromSerializedProperty(prop);

            var tint = dst.FindProperty("assetConfig.scaleConfig.tint");
            if (tint != null) tint.colorValue = DarkTint;

            dst.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Everything an author is allowed to keep. Written once, on creation, and never
        /// again — see the class remarks for why the line sits exactly here.
        /// </summary>
        private static void SeedDesign(MonsterDefinition def, DarkProfile p)
        {
            def.stats = new EntityStats
            {
                hp = p.Hp,
                speed = p.Speed,
                chasingSpeed = p.ChasingSpeed,
                defense = p.Defense,
                power = p.Power,
                meleeRange = p.MeleeRange,
                meleeDamage = p.MeleeDamage,
                meleeCooldown = p.MeleeCooldown,
                aggroRange = p.AggroRange,
                damageDuration = 0.35f,
                damageStopProbability = 0.12f,
                attackWindupSeconds = p.AttackWindup,
                spawnCount = 1,
                spawnPadding = 3,
                spawnMargin = 0,
                deathDisappearTime = 5f,
                feetWidthFactor = 0.5f,
                feetHeightFactor = 0.2f,
                faction = "EVIL",
                chatRange = 0f,
                resistances = System.Array.Empty<ElementResistance>(),
                statusImmunities = System.Array.Empty<StatusEffectKind>(),
            };

            def.fsmSet = DarkFsmSet;
            def.patrolType = "line";
            def.useAttackTelegraph = true;

            def.aiTuning = new AIBehaviourTuning
            {
                // A cone rather than the omniscient 360 default: these hunt, and something
                // that hunts has to be flankable or the dodge is the only counterplay there is.
                fovDegrees = 220f,
                sightMemorySeconds = 4f,
                searchDuration = 7f,
                aggroShareRadius = 12f,
                desiredRange = p.DesiredRange,
                reswingRangeFactor = 1.4f,

                dodgeChance = p.DodgeChance,
                dodgeCooldownSeconds = 1.5f,
                dodgeThreatRadius = 6.5f,
                dodgeDistance = 2.4f,
                dodgeSpeedMultiplier = 2.3f,
            };

            def.autoCast = p.Spells != null && p.Spells.Length > 0;
            def.autoCastList = p.Spells ?? System.Array.Empty<string>();
            def.xpReward = p.XpReward;
            def.level = 1;
        }
    }
}
#endif
