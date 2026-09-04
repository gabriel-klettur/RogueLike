using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Makes one placed building breakable. Attached by <c>BuildingLoader</c> only to
    /// templates that declare a <see cref="DestructionProfile"/>, so the 969 templates that
    /// declare none cost nothing at all — no component, no registry entry, no per-swing work.
    ///
    /// <para>WHY THIS AND NOT A <c>Health</c>. Every damage path in the game finds its
    /// victims through a <c>LayerMask</c>, and a building has to live on Building(14) to
    /// block movement. No mask contains Player(8), NPC(9) and Building(14) at once, so a
    /// <c>Health</c> here would be code nothing could ever reach — which is exactly what
    /// happened to the ice wall's HP for the life of the project.
    /// <see cref="IDestructibleObstacle"/> is the seam that already solved it, and this is
    /// its second implementer: what was a one-off patch for one spell becomes the world's
    /// durability layer.</para>
    /// </summary>
    public class BuildingDurability : MonoBehaviour, IDestructibleObstacle
    {
        private DestructionProfile _profile;
        private BuildingObject _building;
        private int _currentDurability;
        private bool _registered;
        private bool _destroyed;

        /// <summary>
        /// Wall-clock deadline at which a destroyed building comes back. 0 = never, which is
        /// every profile that leaves regrowSeconds at 0 and is the right answer for a house.
        /// </summary>
        private double _regrowAtUnix;

        /// <summary>
        /// A blow that landed. Carries the damage actually dealt after the matrix and the
        /// tool gate, the world point it landed on, and how it was delivered — everything a
        /// feedback layer needs to pick chips, a sound and a camera beat without re-deriving
        /// any of it. Zero damage is still reported: a blow that bounces off is the one the
        /// player most needs told about.
        /// </summary>
        public event Action<int, Vector2, DamageClass> Struck;

        /// <summary>Durability reached zero. Raised before the remains are applied.</summary>
        public event Action<Vector2, DamageClass> Destroyed;

        /// <summary>
        /// A destroyed building came back — the stump grew into a tree again. Raised after the
        /// art has been restored, so a listener that reads the renderers sees the new state.
        /// </summary>
        public event Action Regrown;

        public DestructionProfile Profile => _profile;
        public BuildingObject Building => _building;
        public int CurrentDurability => _currentDurability;
        public int MaxDurability => _profile != null ? Mathf.Max(1, _profile.durability) : 1;
        public bool IsDestroyed => _destroyed;

        /// <summary>
        /// Fraction of durability remaining, 1 down to 0. What a damage-stage visual reads
        /// so the stages follow the profile's own durability instead of a hard-coded count.
        /// </summary>
        public float DurabilityFraction => Mathf.Clamp01((float)_currentDurability / MaxDurability);

        /// <summary>
        /// Wire up and enter the registry. Called explicitly by the loader rather than from
        /// <c>Awake</c> because the profile has to be known before the obstacle is reachable:
        /// registering first would expose an obstacle whose material and durability are not
        /// yet set, and the first swing of that frame would resolve them against nothing.
        /// </summary>
        public void Initialize(DestructionProfile profile, BuildingObject building)
        {
            _profile = profile;
            _building = building;
            _currentDurability = MaxDurability;

            if (_profile == null || _registered) return;
            DestructibleObstacleRegistry.Register(this);
            _registered = true;
        }

        /// <summary>
        /// Restore a partially damaged building, for the save layer. Clamped rather than
        /// trusted: a profile whose durability was rebalanced downward since the run was
        /// saved would otherwise leave a building above its own maximum.
        /// </summary>
        public void RestoreDurability(int value)
        {
            _currentDurability = Mathf.Clamp(value, 0, MaxDurability);
        }

        /// <summary>
        /// Put a building back into the destroyed state a previous session left it in —
        /// silently. No drops, no events, no camera beat: the player felled this tree an hour
        /// ago and must not be paid for it twice, nor watch it fall again on every load.
        ///
        /// <para><paramref name="regrowAtUnix"/> is the wall-clock deadline the save recorded.
        /// Passing a deadline that has ALREADY passed is the normal case for a long absence,
        /// and is handled by leaving the building destroyed for one frame and letting
        /// <see cref="TickRegrow"/> bring it back through the same path a live regrow uses —
        /// rather than a second restore path that would drift from it.</para>
        /// </summary>
        public void RestoreDestroyed(double regrowAtUnix)
        {
            if (_profile == null || _destroyed) return;

            _destroyed = true;
            _currentDurability = 0;
            _regrowAtUnix = regrowAtUnix;

            if (_registered)
            {
                DestructibleObstacleRegistry.Unregister(this);
                _registered = false;
            }

            if (_profile.remainsWalkable)
                BuildingCollisionLoader.ClearColliders(_building);

            ApplyRemains();
        }

        private void Update()
        {
            TickRegrow();
        }

        /// <summary>
        /// Bring a destroyed building back when its deadline passes.
        ///
        /// <para>The deadline is WALL CLOCK, not <c>Time.time</c>, because a regrow has to
        /// survive the session that started it — session time restarts at zero on every load,
        /// so a deadline expressed in it either fires instantly or never, depending only on
        /// the sign of the comparison.</para>
        /// </summary>
        private void TickRegrow()
        {
            if (!_destroyed || _regrowAtUnix <= 0d) return;
            if (WorldDamageService.UnixNow() < _regrowAtUnix) return;

            _regrowAtUnix = 0d;
            Regrow();
        }

        /// <summary>
        /// Undo a felling: the original art comes back, the collision cells come back, the
        /// building re-enters the obstacle registry, and durability is full again.
        ///
        /// <para>It has to go through <c>BuildingObject.RestorePristine</c> rather than
        /// swapping the sprite back, because <c>ApplyRemainsSprite</c> also overwrote the
        /// split ratio and the transform scale the building was assembled with. Putting only
        /// the sprite back would leave a full-height tree drawn at a stump's scale with no
        /// canopy — correct in every field except the three that decide what is on screen.</para>
        /// </summary>
        public void Regrow()
        {
            if (!_destroyed || _building == null) return;

            _destroyed = false;
            _currentDurability = MaxDurability;
            _regrowAtUnix = 0d;

            _building.RestorePristine();

            if (!_registered)
            {
                DestructibleObstacleRegistry.Register(this);
                _registered = true;
            }

            Regrown?.Invoke();
        }

        private void OnDestroy()
        {
            if (!_registered) return;
            DestructibleObstacleRegistry.Unregister(this);
            _registered = false;
        }

        // ── IDestructibleObstacle ──────────────────────────────────────────────────

        public Vector2 ObstaclePosition => DamageableBounds.center;

        /// <summary>
        /// The FOOTPRINT, not the whole sprite. A tree's canopy is drawn several units above
        /// the ground and sorted over the player; measuring the obstacle from it would let a
        /// swing at head height connect with a trunk the character is nowhere near, and would
        /// put the contact point — which the feedback layer places chips at — up in the
        /// leaves. You chop the trunk.
        /// </summary>
        public Bounds ObstacleBounds => DamageableBounds;

        public bool AcceptsDamage => !_destroyed && _profile != null && _currentDurability > 0;

        public void ApplyObstacleDamage(int amount, GameObject attacker, Vector2 contactPoint,
            SpellElement? element)
        {
            if (!AcceptsDamage || amount <= 0) return;

            // The matrix, the tier gate and the never-round-to-zero rule all live in
            // HarvestBlowResolver, because a harvest session asks the identical question and
            // two implementations of it would drift the first time either was tuned.
            var blow = HarvestBlowResolver.Resolve(_profile, attacker, element);
            int dealt = HarvestBlowResolver.Scale(amount, blow.Multiplier);

            _currentDurability -= dealt;
            Struck?.Invoke(dealt, contactPoint, blow.DamageClass);

            if (_currentDurability > 0) return;

            _currentDurability = 0;
            DestroyBuilding(contactPoint, blow.DamageClass);
        }

        // ── Destruction ────────────────────────────────────────────────────────────

        private void DestroyBuilding(Vector2 contactPoint, DamageClass damageClass)
        {
            if (_destroyed) return;
            _destroyed = true;

            // Leave the registry FIRST. Everything below can spawn objects and run callbacks,
            // and an obstacle that is already dying must not be reachable by another swing in
            // the same frame — AcceptsDamage alone would still cost every swing a bounds test
            // for as long as the corpse exists.
            if (_registered)
            {
                DestructibleObstacleRegistry.Unregister(this);
                _registered = false;
            }

            // Armed here rather than in the save layer, so a felling regrows on its own clock
            // whether or not anything is persisting it. The service records the SAME deadline
            // it reads back, which is what keeps a reload from restarting the wait.
            _regrowAtUnix = _profile.regrowSeconds > 0f
                ? WorldDamageService.UnixNow() + _profile.regrowSeconds
                : 0d;

            Destroyed?.Invoke(contactPoint, damageClass);

            HarvestDropResolver.SpawnDrops(_profile.drops, DamageableBounds.center);

            if (_profile.remainsWalkable)
                BuildingCollisionLoader.ClearColliders(_building);

            ApplyRemains();
        }

        /// <summary>
        /// Swap the art for what is left, or remove the building outright when the profile
        /// names no remains.
        ///
        /// <para>The GameObject deliberately SURVIVES when there are remains. It still carries
        /// the instance id, which is what the save layer keys a felled tree by and what a
        /// regrow has to find again — destroying it would make "this specific tree is a stump"
        /// unrepresentable without inventing a second identity for the stump.</para>
        /// </summary>
        private void ApplyRemains()
        {
            if (_building == null)
            {
                Destroy(gameObject);
                return;
            }

            if (string.IsNullOrEmpty(_profile.remainsAssetPath))
            {
                _building.ApplyRemainsSprite(null);
                return;
            }

            var sprite = Resources.Load<Sprite>(_profile.remainsAssetPath);
            if (sprite == null)
            {
                Debug.LogWarning(
                    $"[BuildingDurability] Remains sprite '{_profile.remainsAssetPath}' not found " +
                    $"under Resources (profile '{_profile.name}'). The building was hidden instead.");
            }
            _building.ApplyRemainsSprite(sprite);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private Bounds DamageableBounds
        {
            get
            {
                var footprint = _building != null ? _building.FootprintRenderer : null;
                if (footprint != null && footprint.sprite != null) return footprint.bounds;

                // No renderer yet (the first frame of a programmatic spawn) — a zero-size
                // box at the pivot still passes the range test from touching distance, which
                // is better than an obstacle that cannot be hit at all.
                return new Bounds(transform.position, Vector3.zero);
            }
        }
    }
}
