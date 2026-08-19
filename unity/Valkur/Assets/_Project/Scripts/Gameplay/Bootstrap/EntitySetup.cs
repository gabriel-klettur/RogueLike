using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Spells;
using TMPro;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Configures entity GameObjects from ScriptableObject definitions.
    /// Delegates sprite setup to EntitySpriteHelper and prefab creation to ProjectilePrefabFactory.
    /// </summary>
    public static partial class EntitySetup
    {
        private static readonly int PlayerLayer = SafeNameToLayer("Player");
        private static readonly int NPCLayer = SafeNameToLayer("NPC");

        private static int SafeNameToLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
                Debug.LogWarning($"[EntitySetup] Layer '{layerName}' not found in TagManager! Falling back to Default (0).");
            return layer == -1 ? 0 : layer;
        }

        public static void ConfigurePlayer(GameObject go, PlayerDefinition def)
        {
            // Backwards-compatible all-at-once entry point. Tests call this
            // directly; the runtime spawn path in GameplaySceneSetup steps
            // through the same work via the ConfigurePlayer* helpers below
            // so each chunk can yield to the loading screen between calls.
            ConfigurePlayerVisuals(go, def);
            ConfigurePlayerCombat(go, def);
            ConfigurePlayerSpells(go);
            ConfigurePlayerStats(go, def);
            ConfigurePlayerHUD();

            Debug.Log($"[EntitySetup] Player configured: key={def.playerKey}, HP={def.maxStrength}, MP={def.maxIntelligence}, ATK={def.basicAttack}, SPD={def.basicSpeed}");
        }

        // ── Stepwise player configuration ───────────────────────────────────────
        // Each helper handles one self-contained chunk of player setup so the
        // bootstrap can yield between them and feed sub-stage labels to the
        // loading screen. The split deliberately separates the heavy steps —
        // animation rebinding, spell catalog scan, HUD creation — into their
        // own reports so the user sees the bar advance instead of a single
        // "Spawning player" hang.

        /// <summary>Tags + layer + sprite/material wiring + animator rebind.</summary>
        internal static void ConfigurePlayerVisuals(GameObject go, PlayerDefinition def)
        {
            go.layer = PlayerLayer;
            go.tag = "Player";

            var spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
            bool appliedDataDrivenVisuals = EntityAnimationBinder.ApplyPlayerVisuals(go, def);
            if (!appliedDataDrivenVisuals)
                EntitySpriteHelper.EnsurePlayerSprite(spriteRenderer);
            EntitySpriteHelper.EnsureUnlitMaterial(spriteRenderer);
        }

        /// <summary>Health, movement speed, melee combat, dash ability.</summary>
        internal static void ConfigurePlayerCombat(GameObject go, PlayerDefinition def)
        {
            // Python parity: selected class defines player max HP from max_strength.
            InitHealth(go, def.maxStrength);
            InitPlayerMovement(go, def.basicSpeed);
            InitPlayerCombat(go, def);
        }

        /// <summary>Spell catalog scan + per-spell registration in the spell book.</summary>
        internal static void ConfigurePlayerSpells(GameObject go)
        {
            InitPlayerSpells(go);
        }

        /// <summary>Mana/XP/inventory + death/spirit flow + class marker + register.</summary>
        internal static void ConfigurePlayerStats(GameObject go, PlayerDefinition def)
        {
            InitPlayerStats(go, def);
            InitSharedVisuals(go);
            InitSpiritDeathFlow(go);
            ApplyPlayerClassInitialMarker(go, def.playerKey);

            // Register the player BEFORE building HUDs so any UI singleton's
            // Start() (e.g. InventoryUI) sees a populated EntityRegistry.Player
            // on its first ResolvePlayerRefs call. Otherwise the UI starts in
            // an unwired state and only catches up once the user opens it.
            EntityRegistry.RegisterPlayer(go);
        }

        /// <summary>InventoryUI + SpellBarHUD + HUDIconBar + CombatRangeVisualizer.</summary>
        internal static void ConfigurePlayerHUD()
        {
            EnsureInventoryUI();
            EnsureSpellBarHUD();
            EnsureHUDIconBar();
            EnsureCombatRangeVisualizer();
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            EntityColliderConfigurator.ApplyLayerRecursively(go, NPCLayer);
            go.tag = "Monster";

            var brain = go.GetComponent<FSMMonsterBrain>();
            if (brain != null) brain.Initialize(def);

            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.SetTargetLayers(1 << PlayerLayer);
                combat.SetSlashVfxColor(new Color(0.2f, 0.9f, 0.3f, 0.8f));
            }

            var spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
            bool appliedDataDrivenVisuals = EntityAnimationBinder.ApplyMonsterVisuals(go, def);
            if (!appliedDataDrivenVisuals)
                EntitySpriteHelper.EnsureMonsterSprite(spriteRenderer);
            EntitySpriteHelper.EnsureUnlitMaterial(spriteRenderer);
            EntityColliderConfigurator.ConfigureNpcBodyCollider(go, spriteRenderer);
            InitHealth(go, def.stats.hp);

            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            if (go.GetComponent<StatusEffectManager>() == null)
                go.AddComponent<StatusEffectManager>();

            // Hit flash + knockback. Nothing attached this before, which is why
            // NPCs took damage without ever flashing white.
            if (go.GetComponent<CombatFeedback>() == null)
                go.AddComponent<CombatFeedback>();

            // Tints the sprite gray as the monster dies (Python's death_tint_system).
            // Auto-subscribes to Health.OnDeath in its own OnEnable; just adding the
            // component is enough — InitHealth above ran first so Health is present.
            if (go.GetComponent<GrayscaleDeath>() == null)
                go.AddComponent<GrayscaleDeath>();

            ConfigureMonsterAutoCast(go, def);

            // Minimap dot (monster = red) — uses reflection to avoid Gameplay→UI circular dependency
            ConfigureMinimapDot(go, "Monster", new Color(0.9f, 0.2f, 0.2f, 1f));

            var npcBar = go.GetComponent<WorldHealthBar>();
            if (npcBar == null) npcBar = go.AddComponent<WorldHealthBar>();
            npcBar.SetBarColors(
                new Color(0.9f, 0.25f, 0.2f, 1f),
                new Color(0.95f, 0.15f, 0.1f, 1f));

            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null) ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            EntityRegistry.RegisterMonster(go);
            Debug.Log($"[EntitySetup] Monster configured: {def.displayName}, HP={def.stats.hp}");
        }

        // ── Private helpers ──

        private static void InitHealth(GameObject go, int maxHp)
        {
            var health = go.GetComponent<Health>();
            if (health != null) health.Initialize(maxHp);
        }

        // Adds the two components that drive the spirit/altar revive flow on
        // the player. Idempotent — safe to call on prefabs that already carry
        // them (the GetComponent guards skip in that case).
        private static void InitSpiritDeathFlow(GameObject go)
        {
            if (go.GetComponent<PlayerSpiritState>()   == null) go.AddComponent<PlayerSpiritState>();
            if (go.GetComponent<PlayerSpiritVisuals>() == null) go.AddComponent<PlayerSpiritVisuals>();
        }

        private static void InitPlayerMovement(GameObject go, float speed)
        {
            var controller = go.GetComponent<PlayerController>();
            if (controller != null) controller.SetMoveSpeed(speed);
        }

        private static void InitPlayerCombat(GameObject go, PlayerDefinition def)
        {
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.Initialize(def.basicAttack, 0.5f, 1.5f);
                combat.SetTargetLayers(1 << NPCLayer);
            }

            var dash = go.GetComponent<DashAbility>();
            if (dash == null) dash = go.AddComponent<DashAbility>();
            dash.SetTargetLayers(1 << NPCLayer);
        }

        /// <summary>
        /// Cached reference set by GameplaySceneSetup before player spawn.
        /// </summary>
        private static SpellCatalog _spellCatalog;

        /// <summary>
        /// Set the spell catalog before ConfigurePlayer so all spells are available.
        /// Called from GameplaySceneSetup.
        /// </summary>
        public static void SetSpellCatalog(SpellCatalog catalog)
        {
            _spellCatalog = catalog;
        }

        private static void InitPlayerSpells(GameObject go)
        {
            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) return;

            caster.SetTargetLayers(1 << NPCLayer);
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);

            // Primary: use the injected SpellCatalog
            SpellDefinition[] allSpells = null;
            if (_spellCatalog != null && _spellCatalog.Count > 0)
            {
                allSpells = _spellCatalog.AllSpells;
            }

            // Fallback: scan via AssetDatabase in editor, Resources in build
            if (allSpells == null || allSpells.Length == 0)
            {
#if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets("t:SpellDefinition", new[] { "Assets/_Project/Data/Catalogs/Spells" });
                var list = new System.Collections.Generic.List<SpellDefinition>(guids.Length);
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var def = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                    if (def != null) list.Add(def);
                }
                allSpells = list.ToArray();
                if (allSpells.Length > 0)
                    Debug.Log($"[EntitySetup] Loaded {allSpells.Length} spells via AssetDatabase fallback.");
#else
                allSpells = Resources.LoadAll<SpellDefinition>("Catalogs/Spells");
#endif
            }

            if (allSpells == null || allSpells.Length == 0)
            {
                Debug.LogWarning("[EntitySetup] No SpellDefinition assets found! Falling back to fireball only.");
                caster.SetSpell(0, ProjectilePrefabFactory.GetFireballSpell());
                return;
            }

            int registered = 0;
            foreach (var spell in allSpells)
            {
                if (spell == null || string.IsNullOrEmpty(spell.spellKey)) continue;
                caster.RegisterSpell(spell.spellKey, spell);
                registered++;
            }

            // Set slot 0 to fireball for backward compatibility (LMB)
            var fireball = ProjectilePrefabFactory.GetFireballSpell();
            if (fireball != null)
                caster.SetSpell(0, fireball);

            Debug.Log($"[EntitySetup] Registered {registered}/{allSpells.Length} spells in spell book.");
        }

        // ── Monster auto-cast wiring ────────────────────────────────────────────
        // Reads MonsterDefinition.autoCast + autoCastList and:
        //   1. Ensures a SpellCaster + NPCAutoCast on the monster GameObject.
        //   2. Looks up each spell key in the SpellCatalog (set on the player path
        //      via SetSpellCatalog) — silently skips unknown keys with a warning.
        //   3. Drops the resolved SpellDefinitions into SpellCaster slots 0..N-1
        //      (capped at SlotCount; everything above lands only in the spell book).
        //   4. Wipes any inspector-authored NPCAutoCast entries and replaces them
        //      with one entry per registered slot using the default period/jitter.
        //
        // Default period 3 s + jitter 0.5 s mirrors the Python AutoCastComponent
        // legacy single-entry path (period_s = 2.0 with similar jitter); 3 s is
        // slightly more conservative for first-port balancing and matches the
        // pre-existing inspector default on NPCAutoCast.
        //
        // No-op for monsters where autoCast is false or autoCastList is empty,
        // so existing melee-only NPCs are not affected.
        internal static void ConfigureMonsterAutoCast(GameObject go, MonsterDefinition def)
        {
            if (def == null || !def.autoCast) return;
            if (def.autoCastList == null || def.autoCastList.Length == 0) return;

            if (_spellCatalog == null)
            {
                Debug.LogWarning($"[EntitySetup] Monster '{def.monsterKey}' has autoCast enabled " +
                                 "but no SpellCatalog was injected via SetSpellCatalog. Skipping.");
                return;
            }

            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) caster = go.AddComponent<SpellCaster>();
            caster.SetTargetLayers(1 << PlayerLayer);
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);

            var auto = go.GetComponent<NPCAutoCast>();
            if (auto == null) auto = go.AddComponent<NPCAutoCast>();
            auto.Clear();

            int registered = 0;
            int slotCount  = caster.SlotCount;
            for (int i = 0; i < def.autoCastList.Length; i++)
            {
                string key = def.autoCastList[i];
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!_spellCatalog.TryGet(key, out var spell) || spell == null)
                {
                    Debug.LogWarning($"[EntitySetup] Monster '{def.monsterKey}' references unknown " +
                                     $"spell '{key}' in autoCastList — skipping.");
                    continue;
                }

                // Always register in the spell book so TryCastByKey works even when
                // the spell falls outside the slot count.
                caster.RegisterSpell(spell.spellKey, spell);

                if (registered < slotCount)
                {
                    caster.SetSpell(registered, spell);
                    auto.AddEntry(registered, periodSeconds: 3f, jitter: 0.5f);
                    registered++;
                }
            }

            Debug.Log($"[EntitySetup] Monster '{def.monsterKey}' auto-cast: " +
                      $"{registered}/{def.autoCastList.Length} spell(s) wired.");
        }

    }
}
