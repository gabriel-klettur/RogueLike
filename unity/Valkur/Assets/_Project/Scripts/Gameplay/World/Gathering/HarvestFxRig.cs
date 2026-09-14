using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.World
{
    /// <summary>The MonoBehaviour behind <see cref="HarvestFx"/>. Owns the systems and the flash pool.</summary>
    public sealed class HarvestFxRig : MonoBehaviour
    {
        public ParticleSystem Chips { get; private set; }
        public ParticleSystem LeafSystem { get; private set; }
        public ParticleSystem DustSystem { get; private set; }

        private SpriteRenderer[] _sparks;
        private float[] _sparkUntil;
        private int _nextSpark;

        public void Build(int sparkPool)
        {
            Chips = MakeSystem("Chips", HarvestFxTextures.Chip, maxParticles: 256, gravity: 2.4f, lifetime: new Vector2(0.3f, 0.55f),
                size: new Vector2(0.06f, 0.13f), speed: new Vector2(1.8f, 3.6f), spin: true, drag: 0f);
            LeafSystem = MakeSystem("Leaves", HarvestFxTextures.Leaf, maxParticles: 320, gravity: 0.12f, lifetime: new Vector2(1.0f, 1.9f),
                size: new Vector2(0.10f, 0.19f), speed: new Vector2(0.2f, 0.9f), spin: true, drag: 1.6f);
            DustSystem = MakeSystem("Dust", HarvestFxTextures.Puff, maxParticles: 160, gravity: -0.05f, lifetime: new Vector2(0.6f, 1.2f),
                size: new Vector2(0.45f, 1.1f), speed: new Vector2(0.3f, 1.1f), spin: true, drag: 2.2f);

            _sparks = new SpriteRenderer[sparkPool];
            _sparkUntil = new float[sparkPool];
            for (int i = 0; i < sparkPool; i++)
            {
                var go = new GameObject("Flash" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.HotCore;
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = 12;
                sr.enabled = false;
                _sparks[i] = sr;
            }
            enabled = false;
        }

        private ParticleSystem MakeSystem(string name, Texture2D texture, int maxParticles, float gravity, Vector2 lifetime,
            Vector2 size, Vector2 speed, bool spin, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            // AddComponent starts the system (playOnAwake), and main.duration cannot be written
            // while it plays. Stop, configure, play.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.1f;

            if (spin)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            }

            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = drag;
                limit.dampen = 0f;
                limit.limit = 50f;
            }

            var alpha = ps.colorOverLifetime;
            alpha.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            alpha.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 10;
            renderer.sharedMaterial = ParticleMaterialCache.Get(texture != null ? texture : Texture2D.whiteTexture, additive: false);

            ps.Play();
            return ps;
        }

        public void Emit(ParticleSystem ps, Vector3 at, Color color, int count, Vector2 away, float speed)
        {
            if (ps == null) return;
            Vector2 dir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.up;
            for (int i = 0; i < count; i++)
            {
                // A cone around "away from the axe" with an upward kick, so chips fly off the
                // struck side and arc down rather than exploding symmetrically from the trunk.
                Vector2 jitter = Random.insideUnitCircle * 0.8f;
                Vector2 v = (dir + Vector2.up * 0.9f + jitter).normalized * speed * Random.Range(0.6f, 1.2f);
                var p = new ParticleSystem.EmitParams
                {
                    position = at + (Vector3)(Random.insideUnitCircle * 0.08f),
                    velocity = v,
                    startColor = Vary(color, 0.12f),
                    applyShapeToPosition = false,
                };
                ps.Emit(p, 1);
            }
        }

        public void EmitArea(ParticleSystem ps, Vector3 at, Color color, int count, float spread)
        {
            if (ps == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spread;
                var p = new ParticleSystem.EmitParams
                {
                    position = at + new Vector3(offset.x, offset.y * 0.6f, 0f),
                    velocity = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.2f, 0.7f), 0f),
                    startColor = Vary(color, 0.18f),
                    applyShapeToPosition = false,
                };
                ps.Emit(p, 1);
            }
        }

        public void Flash(Vector3 at, Color color, float size, float seconds)
        {
            var sr = _sparks[_nextSpark];
            _nextSpark = (_nextSpark + 1) % _sparks.Length;
            sr.transform.position = at;
            sr.transform.localScale = Vector3.one * size;
            sr.color = color;
            sr.enabled = true;
            _sparkUntil[System.Array.IndexOf(_sparks, sr)] = Time.time + seconds;
            enabled = true;
        }

        private void LateUpdate()
        {
            bool any = false;
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_sparks[i].enabled) continue;
                if (Time.time >= _sparkUntil[i]) _sparks[i].enabled = false;
                else any = true;
            }
            if (!any) enabled = false;
        }

        private static Color Vary(Color c, float amount)
        {
            float k = 1f + Random.Range(-amount, amount);
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }
    }
}
