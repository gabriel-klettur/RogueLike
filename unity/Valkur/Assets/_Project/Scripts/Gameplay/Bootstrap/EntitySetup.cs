using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
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
            go.layer = PlayerLayer;
            go.tag = "Player";

            var spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
            bool appliedDataDrivenVisuals = EntityAnimationBinder.ApplyPlayerVisuals(go, def);
            if (!appliedDataDrivenVisuals)
                EntitySpriteHelper.EnsurePlayerSprite(spriteRenderer);
            EntitySpriteHelper.EnsureUnlitMaterial(spriteRenderer);

            // Python parity: selected class defines player max HP from max_strength.
            InitHealth(go, def.maxStrength);
            InitPlayerMovement(go, def.basicSpeed);
            InitPlayerCombat(go, def);
            InitPlayerSpells(go);
            InitPlayerStats(go, def);
            InitSharedVisuals(go);
            ApplyPlayerClassInitialMarker(go, def.playerKey);

            EnsureInventoryUI();
            EnsureSpellBarHUD();
            EnsureMinimizedTray();
            EnsureCombatRangeVisualizer();

            EntityRegistry.RegisterPlayer(go);
            Debug.Log($"[EntitySetup] Player configured: key={def.playerKey}, HP={def.maxStrength}, MP={def.maxIntelligence}, ATK={def.basicAttack}, SPD={def.basicSpeed}");
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            go.layer = NPCLayer;
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
            CompensateColliderForScale(go);
            InitHealth(go, def.stats.hp);

            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            if (go.GetComponent<StatusEffectManager>() == null)
                go.AddComponent<StatusEffectManager>();

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

        /// <summary>
        /// After EntityAnimationBinder scales the root transform for Python-parity visual sizing,
        /// the CircleCollider2D radius must be compensated so its world-space size stays constant.
        /// </summary>
        private static void CompensateColliderForScale(GameObject go)
        {
            float scale = go.transform.localScale.x;
            if (Mathf.Approximately(scale, 1f) || scale <= 0f) return;

            var circle = go.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius /= scale;
        }

        private static void InitHealth(GameObject go, int maxHp)
        {
            var health = go.GetComponent<Health>();
            if (health != null) health.Initialize(maxHp);
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

    }
}