using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Per-frame half of the summon arrival: the five beats, the hand-over of control to the creature, and the guarantee that a rig torn down early still leaves a standing, visible, thinking ally.
    /// </summary>
    internal sealed partial class SummonRiseFX
    {

        private void Update()
        {
            _age += Time.deltaTime;

            if (!_spawned && _age >= T_SPAWN) SpawnBody();
            if (!_released && _age >= T_STANDING) ReleaseBody();

            UpdateSigil();
            UpdateTendrils();
            UpdateBody();
            UpdateClods();
            UpdateMotes();
            UpdateLight();

            if (_age >= T_END) Destroy(gameObject);
        }

        private void SpawnBody()
        {
            _spawned = true;

            // Everything about siding, masks, minimap colour, health bar and the dismissal
            // hook lives in AlliedSummonService. This class only owns the entrance.
            _creature = AlliedSummonService.Summon(_definition, _position, _lifetime, _healthScale);
            if (_creature == null) return;

            SpellEffectRegistry.Track(_creature, _spell, _caster, _enforceCap);

            // Off for the length of the rise, exactly as AllyDismissFX switches them off for
            // the length of the sinking.
            var brain = _creature.GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null) brain.enabled = false;
            foreach (var col in _creature.GetComponentsInChildren<Collider2D>()) col.enabled = false;
            var rb = _creature.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;

            _bodyRoot = _creature.transform;
            _bodyRestPosition = _bodyRoot.position;
            _bodyRoot.position = _bodyRestPosition + Vector3.down * RISE_DEPTH;

            // TintLayer.Teleport rather than Spirit: AlliedSummonService already owns Spirit
            // for the ally rim, and the stack MULTIPLIES its layers, so the rise fade and the
            // green tint compose instead of overwriting each other. There is no teleport on a
            // creature that is being summoned, so the layer is free.
            _bodyTint = SpriteTintStack.Attach(_creature);
            _bodyTint?.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, 0.15f));
        }

        private void ReleaseBody()
        {
            _released = true;
            if (_creature == null) return;

            _bodyRoot.position = _bodyRestPosition;
            _bodyTint?.Clear(TintLayer.Teleport);

            foreach (var col in _creature.GetComponentsInChildren<Collider2D>()) col.enabled = true;
            var brain = _creature.GetComponent<FSM.FSMMonsterBrain>();
            if (brain != null) brain.enabled = true;

            // The lifetime rim is a separate concern from the entrance, and it outlives this
            // object by the whole of the summon's service.
            _creature.AddComponent<SummonController>()
                     .Initialize(_palette, _lifetime);
        }

        private void UpdateSigil()
        {
            if (_ring != null)
            {
                float k = Mathf.Clamp01(_age / T_STANDING);
                float eased = 1f - (1f - k) * (1f - k);
                _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.25f, SIGIL_RADIUS / 0.39f, eased);
                _ring.transform.localRotation = Quaternion.Euler(0f, 0f, k * 90f);
                _ring.color = WithAlpha(_palette.Leaf, 0.80f * (1f - Mathf.SmoothStep(0.7f, 1f, _age / T_END)));
            }

            if (_burst == null) return;
            // The burst is the EVENT layer: it exists only across the moment the surface
            // breaks, which is about 30 % of the sequence.
            float b = 1f - Mathf.Clamp01(Mathf.Abs(_age - T_THROW) / 0.16f);
            _burst.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.4f, 1f - b);
            _burst.color = WithAlpha(_palette.Sap, b * b * 0.75f);
        }

        private void UpdateTendrils()
        {
            if (_tendrils == null) return;

            // Up with the sigil, withdrawn once the creature is standing: the roots parted to
            // let something through, they are not scenery.
            float grow = Mathf.Clamp01(_age / T_THROW);
            float withdraw = 1f - Mathf.Clamp01((_age - T_STANDING) / (T_END - T_STANDING));
            float height = Mathf.Lerp(0.05f, 0.85f, grow) * withdraw;

            for (int i = 0; i < _tendrils.Length; i++)
            {
                var t = _tendrils[i].transform;
                t.localScale = new Vector3(0.7f, Mathf.Max(0.01f, height), 1f);
                _tendrils[i].color = WithAlpha(_palette.Bark, Mathf.Clamp01(grow * 2f) * withdraw);
            }
        }

        private void UpdateBody()
        {
            if (_creature == null || _released || _bodyRoot == null) return;

            float k = Mathf.Clamp01((_age - T_SPAWN) / (T_STANDING - T_SPAWN));
            float eased = k * k * (3f - 2f * k);
            _bodyRoot.position = _bodyRestPosition + Vector3.down * (RISE_DEPTH * (1f - eased));
            _bodyTint?.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, Mathf.Lerp(0.15f, 1f, eased)));
        }

        private void UpdateClods()
        {
            if (_clods == null) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _clods.Length; i++)
            {
                var t = _clods[i].transform;

                if (_age < T_SPAWN)
                {
                    _clods[i].color = WithAlpha(_palette.Soil, Mathf.Clamp01(_age / T_SPAWN));
                    continue;
                }

                if (_age < T_THROW)
                {
                    // Heaping while the body pushes up underneath.
                    float push = Mathf.Clamp01((_age - T_SPAWN) / (T_THROW - T_SPAWN));
                    t.localPosition = _clodRest[i] + Vector3.up * (push * 0.22f);
                    _clods[i].color = WithAlpha(_palette.Soil, 1f);
                    continue;
                }

                // Thrown, and falling back. Opaque earth, so it fades by shrinking as much as
                // by dimming — a chip of soil does not glow out.
                _clodVelocity[i] += Vector2.down * (11f * dt);
                t.localPosition += (Vector3)(_clodVelocity[i] * dt);
                t.localRotation *= Quaternion.Euler(0f, 0f, _clodVelocity[i].x * 220f * dt);

                float fade = 1f - Mathf.Clamp01((_age - T_THROW) / (T_END - T_THROW));
                _clods[i].color = WithAlpha(_palette.Soil, fade);
            }
        }

        private void UpdateMotes()
        {
            if (_motes == null) return;

            float k = Mathf.Clamp01((_age - T_SPAWN) / (T_END - T_SPAWN));
            for (int i = 0; i < _motes.Length; i++)
            {
                _motes[i].transform.localPosition += _moteDrift[i] * Time.deltaTime;
                _motes[i].color = WithAlpha(_palette.Sap, Mathf.Sin(k * Mathf.PI) * 0.55f);
            }
        }

        private void UpdateLight()
        {
            if (_light == null) return;
            float k = Mathf.Clamp01(_age / T_END);
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?
                    .SetValue(_light, Mathf.Sin(k * Mathf.PI) * 2.2f);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

        /// <summary>
        /// Whatever happened — sequence finished, scene torn down, the creature killed while
        /// still coming up — the body must not be left sunk, invisible, or brainless.
        /// </summary>
        private void OnDestroy()
        {
            if (!_spawned || _released || _creature == null) return;
            ReleaseBody();
        }

    }
}
