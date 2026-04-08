using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Sustained laser beam component attached temporarily to the caster.
    /// Renders a LineRenderer beam toward the cast direction and deals damage on ticks.
    /// Maps to Python's LaserBeamEmitterSystem: particles along line, per-tick damage, duration-limited.
    /// 
    /// Destroyed automatically when the beam duration expires.
    /// </summary>
    public class LaserBeamController : MonoBehaviour
    {
        private const float TICK_INTERVAL = 0.25f;
        private const float DEFAULT_RANGE = 10f;
        private const float DEFAULT_DURATION = 2f;
        private const float DEFAULT_BEAM_WIDTH = 0.12f;
        private const int PARTICLE_EMIT_FRAMES = 3;

        private LineRenderer _lineRenderer;
        private Material _beamMaterial;
        private SpellContext _ctx;
        private bool _running;

        /// <summary>Starts the beam coroutine. Call immediately after AddComponent.</summary>
        public void Begin(SpellContext ctx)
        {
            _ctx = ctx;
            BuildVisual(ctx);
            StartCoroutine(RunBeam());
        }

        private void BuildVisual(SpellContext ctx)
        {
            var beamGo = new GameObject("LaserBeam_Visual");
            beamGo.transform.SetParent(transform, false);

            _lineRenderer = beamGo.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 2;

            float width = DEFAULT_BEAM_WIDTH * (ctx.Spell.scale > 0 ? ctx.Spell.scale : 1f);
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width * 0.4f;

            Color col = ctx.Spell.particleColor != Color.clear && ctx.Spell.particleColor.a > 0
                ? ctx.Spell.particleColor
                : new Color(0f, 0.9f, 1f, 1f);

            _lineRenderer.startColor = col;
            _lineRenderer.endColor = new Color(col.r, col.g, col.b, 0f);

            _beamMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            _beamMaterial.hideFlags = HideFlags.HideAndDontSave;
            _lineRenderer.sharedMaterial = _beamMaterial;

            _lineRenderer.sortingLayerName = "VFX";
            _lineRenderer.sortingOrder = 5;
        }

        private IEnumerator RunBeam()
        {
            _running = true;

            float duration = _ctx.Spell.channelDuration > 0 ? _ctx.Spell.channelDuration : DEFAULT_DURATION;
            float range = _ctx.Spell.range > 0 ? _ctx.Spell.range : DEFAULT_RANGE;
            float beamHalfWidth = DEFAULT_BEAM_WIDTH * (_ctx.Spell.scale > 0 ? _ctx.Spell.scale : 1f);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(_ctx.Spell.damage > 0 ? _ctx.Spell.damage : 1f));

            Color particleColor = _ctx.Spell.particleColor != Color.clear && _ctx.Spell.particleColor.a > 0
                ? _ctx.Spell.particleColor
                : new Color(0f, 0.9f, 1f, 1f);

            float elapsed = 0f;
            float nextTick = 0f;
            var damagedThisTick = new HashSet<GameObject>();

            // Determine the blocking layers (world geometry, buildings)
            int blockMask = LayerMask.GetMask("World", "Building");

            while (elapsed < duration)
            {
                // Resolve current beam direction: prefer PlayerController.FacingDirection if available
                Vector2 dir = ResolveDirection();
                Vector2 origin = (Vector2)transform.position;

                // Find beam endpoint, stopping at solid obstacles
                float actualRange = range;
                var wallHit = Physics2D.Raycast(origin, dir, range, blockMask);
                Vector2 end = wallHit.collider != null ? wallHit.point : origin + dir * range;

                // Update LineRenderer
                if (_lineRenderer != null)
                {
                    _lineRenderer.SetPosition(0, origin);
                    _lineRenderer.SetPosition(1, end);
                }

                // Damage tick
                elapsed += Time.deltaTime;
                if (elapsed >= nextTick)
                {
                    nextTick += TICK_INTERVAL;
                    damagedThisTick.Clear();

                    actualRange = Vector2.Distance(origin, end);
                    Vector2 capsuleCenter = origin + dir * (actualRange * 0.5f);
                    float angle = Vector2.SignedAngle(Vector2.right, dir);

                    var hits = Physics2D.OverlapCapsuleAll(
                        capsuleCenter,
                        new Vector2(actualRange, beamHalfWidth * 2f),
                        CapsuleDirection2D.Horizontal,
                        angle,
                        _ctx.TargetLayers
                    );

                    foreach (var c in hits)
                    {
                        if (c.gameObject == gameObject) continue;
                        if (damagedThisTick.Contains(c.gameObject)) continue;

                        var health = c.GetComponent<Health>();
                        if (health != null && !health.IsDead)
                        {
                            health.TakeDamage(dmg);
                            damagedThisTick.Add(c.gameObject);
                        }
                    }
                }

                // Particle VFX along beam (every N frames)
                if (Time.frameCount % PARTICLE_EMIT_FRAMES == 0 && VFXManager.Instance != null)
                {
                    float t = Random.value;
                    Vector2 ppos = Vector2.Lerp(origin, end, t);
                    VFXManager.Instance.SpawnImpact(ppos, particleColor, 0.12f, 0.4f);
                }

                yield return null;
            }

            _running = false;
            Destroy(gameObject.GetComponentInChildren<LineRenderer>()?.gameObject);
            Destroy(this);
        }

        private Vector2 ResolveDirection()
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null)
                return pc.FacingDirection;
            return _ctx.Direction;
        }

        private void OnDestroy()
        {
            if (_beamMaterial != null)
                Destroy(_beamMaterial);
        }
    }
}
