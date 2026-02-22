using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Rendering;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Utility to configure entity GameObjects from ScriptableObject definitions.
    /// Used by spawners and scene setup to wire prefabs at runtime.
    /// Handles layer assignment, combat targeting, and component initialization.
    /// </summary>
    public static class EntitySetup
    {
        private static readonly int PlayerLayer = LayerMask.NameToLayer("Player");
        private static readonly int NPCLayer = LayerMask.NameToLayer("NPC");
        private static readonly int ProjectileLayer = LayerMask.NameToLayer("Projectile");

        private static Sprite _playerSprite;
        private static Sprite _monsterSprite;

        public static void ConfigurePlayer(GameObject go, PlayerDefinition def)
        {
            // Layer & tag
            go.layer = PlayerLayer;
            go.tag = "Player";

            // Assign placeholder sprite if none set
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite == null)
            {
                if (_playerSprite == null)
                    _playerSprite = CreatePlaceholderSprite(new Color(0.2f, 0.47f, 0.86f));
                if (_playerSprite != null)
                    sr.sprite = _playerSprite;
                EnsureUnlitMaterial(sr);
            }

            var health = go.GetComponent<Health>();
            if (health != null)
                health.Initialize(def.initialStrength);

            var controller = go.GetComponent<PlayerController>();
            if (controller != null)
                controller.SetMoveSpeed(def.basicSpeed);

            // MeleeCombat targets NPCs
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.Initialize(def.basicAttack, 0.5f, 1.5f);
                combat.SetTargetLayers(1 << NPCLayer);
            }

            // SpellCaster targets NPCs + assign fireball to slot 0
            var caster = go.GetComponent<Spells.SpellCaster>();
            if (caster != null)
            {
                caster.SetTargetLayers(1 << NPCLayer);
                caster.SetSpell(0, CreateFireballSpell());
            }

            // DashAbility targets NPCs (collision damage during dash)
            var dash = go.GetComponent<DashAbility>();
            if (dash != null)
                dash.SetTargetLayers(1 << NPCLayer);

            // Inventory
            var inventory = go.GetComponent<Inventory.Inventory>();
            if (inventory != null)
                inventory.Initialize(20);

            // Floating damage numbers
            if (go.GetComponent<Combat.FloatingDamageSpawner>() == null)
                go.AddComponent<Combat.FloatingDamageSpawner>();

            // World-space health bar (player bar hidden at full HP by default)
            var playerBar = go.GetComponent<Combat.WorldHealthBar>();
            if (playerBar == null)
                playerBar = go.AddComponent<Combat.WorldHealthBar>();
            playerBar.SetBarColors(
                new Color(0.2f, 0.9f, 0.25f, 1f),   // green fill
                new Color(0.95f, 0.85f, 0.15f, 1f)); // yellow when low

            // Y-sort rendering (player layer)
            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null)
                ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            // Mana
            var mana = go.GetComponent<Mana>();
            if (mana == null)
                mana = go.AddComponent<Mana>();
            mana.Initialize(def.initialIntelligence * 10, def.manaRegenPerSecond);

            // Experience
            var xp = go.GetComponent<Experience>();
            if (xp == null)
                xp = go.AddComponent<Experience>();

            // Pickup system
            if (go.GetComponent<Inventory.PickupSystem>() == null)
                go.AddComponent<Inventory.PickupSystem>();

            // Inventory UI (singleton, created once)
            EnsureInventoryUI();

            // Facing direction indicator (visual arrow toward mouse)
            if (go.GetComponent<Combat.FacingIndicator>() == null)
                go.AddComponent<Combat.FacingIndicator>();

            // Combat range visualizer (F2 toggle, singleton)
            EnsureCombatRangeVisualizer();

            // Ensure SpellCaster has a fireball projectile prefab
            if (caster != null)
                EnsureFireballProjectilePrefab(caster);

            EntityRegistry.RegisterPlayer(go);

            Debug.Log($"[EntitySetup] Player configured: {def.displayName}, HP={def.initialStrength}, Speed={def.basicSpeed}");
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            // Layer & tag
            go.layer = NPCLayer;
            go.tag = "Monster";

            // Prefer FSMMonsterBrain over legacy MonsterAI
            var brain = go.GetComponent<FSMMonsterBrain>();
            if (brain != null)
            {
                brain.Initialize(def);
            }
            else
            {
                var ai = go.GetComponent<MonsterAI>();
                if (ai != null)
                    ai.InitializeFromDefinition(def);
            }

            // MeleeCombat targets Player + green slash VFX
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.SetTargetLayers(1 << PlayerLayer);
                combat.SetSlashVfxColor(new Color(0.2f, 0.9f, 0.3f, 0.8f));
            }

            // Assign placeholder sprite if none set
            var sr = go.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite == null)
            {
                if (_monsterSprite == null)
                    _monsterSprite = CreatePlaceholderSprite(new Color(0.78f, 0.2f, 0.2f));
                if (_monsterSprite != null)
                    sr.sprite = _monsterSprite;
                EnsureUnlitMaterial(sr);
            }

            // Ensure monsters have Health initialized from definition stats
            var health = go.GetComponent<Health>();
            if (health != null)
                health.Initialize(def.stats.hp);

            // Floating damage numbers
            if (go.GetComponent<Combat.FloatingDamageSpawner>() == null)
                go.AddComponent<Combat.FloatingDamageSpawner>();

            // World-space health bar (monsters show bar when damaged, red fill)
            var npcBar = go.GetComponent<Combat.WorldHealthBar>();
            if (npcBar == null)
                npcBar = go.AddComponent<Combat.WorldHealthBar>();
            npcBar.SetBarColors(
                new Color(0.9f, 0.25f, 0.2f, 1f),    // red fill
                new Color(0.95f, 0.15f, 0.1f, 1f));   // darker red when low

            // Y-sort rendering (entity layer)
            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null)
                ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            EntityRegistry.RegisterMonster(go);

            Debug.Log($"[EntitySetup] Monster configured: {def.displayName}, HP={def.stats.hp}");
        }

        private static void EnsureInventoryUI()
        {
            if (InventoryUI.Instance != null) return;
            var uiGo = new GameObject("InventoryUI");
            uiGo.AddComponent<InventoryUI>();
        }

        private static void EnsureCombatRangeVisualizer()
        {
            if (Combat.CombatRangeVisualizer.Instance != null) return;
            var vizGo = new GameObject("CombatRangeVisualizer");
            vizGo.AddComponent<Combat.CombatRangeVisualizer>();
        }

        private static GameObject _fireballPrefab;

        private static void EnsureFireballProjectilePrefab(SpellCaster caster)
        {
            if (_fireballPrefab == null)
            {
                _fireballPrefab = new GameObject("FireballPrefab");
                _fireballPrefab.SetActive(false);

                // Rigidbody2D (required by Projectile)
                var rb = _fireballPrefab.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.freezeRotation = true;

                // Collider (trigger for hit detection)
                var col = _fireballPrefab.AddComponent<CircleCollider2D>();
                col.radius = 0.15f;
                col.isTrigger = true;

                // SpriteRenderer placeholder (FireballVisual will override)
                var sr = _fireballPrefab.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = Core.SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = Core.SortingConfig.Z_SKY;

                // Projectile component
                _fireballPrefab.AddComponent<Projectile>();

                // Fireball visual (procedural glow + flicker)
                _fireballPrefab.AddComponent<FireballVisual>();

                // Layer: Projectile
                _fireballPrefab.layer = ProjectileLayer;

                Object.DontDestroyOnLoad(_fireballPrefab);
            }

            caster.SetProjectilePrefab(_fireballPrefab);
        }

        private static SpellDefinition _fireballSpell;

        private static SpellDefinition CreateFireballSpell()
        {
            if (_fireballSpell != null) return _fireballSpell;

            _fireballSpell = ScriptableObject.CreateInstance<SpellDefinition>();
            _fireballSpell.spellKey = "fireball";
            _fireballSpell.displayName = "Fireball";
            _fireballSpell.type = SpellType.Projectile;
            _fireballSpell.manaCost = 0f;
            _fireballSpell.prepareDuration = 0f;
            _fireballSpell.channelDuration = 0f;
            _fireballSpell.cooldownDuration = 0.4f;
            _fireballSpell.speed = 8f;
            _fireballSpell.damage = 15f;
            _fireballSpell.range = 12f;
            _fireballSpell.lifetime = 3f;
            _fireballSpell.particleColor = new Color(1f, 0.5f, 0.1f, 1f);
            return _fireballSpell;
        }

        private static Material _unlitSpriteMaterial;

        private static Sprite CreatePlaceholderSprite(Color color)
        {
            var tex = new Texture2D(32, 32);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            // PPU=32 -> 32px / 32ppu = 1 world unit
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        }

        /// <summary>
        /// Force SpriteRenderer to use unlit material so sprites render without 2D lights.
        /// </summary>
        private static void EnsureUnlitMaterial(SpriteRenderer sr)
        {
            if (sr == null) return;
            if (_unlitSpriteMaterial == null)
                _unlitSpriteMaterial = new Material(Shader.Find("Sprites/Default"));
            sr.material = _unlitSpriteMaterial;
        }
    }
}
