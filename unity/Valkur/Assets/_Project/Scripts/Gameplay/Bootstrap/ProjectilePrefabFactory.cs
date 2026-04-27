using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Creates and caches runtime prefabs and spell definitions for projectiles.
    /// Extracted from EntitySetup to isolate prefab construction concerns.
    /// </summary>
    public static class ProjectilePrefabFactory
    {
        private static GameObject _fireballPrefab;
        private static SpellDefinition _fireballSpell;
        private static readonly int ProjectileLayer = LayerMask.NameToLayer("Projectile") != -1
            ? LayerMask.NameToLayer("Projectile") : 0;

        public static void EnsureFireballPrefab(SpellCaster caster)
        {
            if (_fireballPrefab == null)
            {
                _fireballPrefab = new GameObject("FireballPrefab");
                _fireballPrefab.SetActive(false);

                var rb = _fireballPrefab.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.freezeRotation = true;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                var col = _fireballPrefab.AddComponent<CircleCollider2D>();
                col.radius = 0.15f;
                col.isTrigger = true;

                var sr = _fireballPrefab.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = SortingConfig.Z_SKY;

                _fireballPrefab.AddComponent<Projectile>();
                _fireballPrefab.AddComponent<FireballVisual>();
                _fireballPrefab.layer = ProjectileLayer;

                Object.DontDestroyOnLoad(_fireballPrefab);
            }

            caster.SetProjectilePrefab(_fireballPrefab);
        }

        public static SpellDefinition GetFireballSpell()
        {
            if (_fireballSpell != null) return _fireballSpell;

            _fireballSpell = ScriptableObject.CreateInstance<SpellDefinition>();
            _fireballSpell.spellKey = "fireball";
            _fireballSpell.displayName = "Fireball";
            _fireballSpell.type = SpellType.Projectile;
            _fireballSpell.manaCost = 1f;
            _fireballSpell.prepareDuration = 0f;
            _fireballSpell.channelDuration = 0f;
            _fireballSpell.cooldownDuration = 0.4f;
            _fireballSpell.speed = 1.5f;
            _fireballSpell.damage = 15f;
            _fireballSpell.range = 12f;
            _fireballSpell.lifetime = 3f;
            _fireballSpell.particleColor = new Color(1f, 0.5f, 0.1f, 1f);
            return _fireballSpell;
        }
    }
}
