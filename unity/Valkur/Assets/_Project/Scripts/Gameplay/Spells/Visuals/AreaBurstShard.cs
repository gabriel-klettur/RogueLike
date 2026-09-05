using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One thing that comes UP out of the floor, stands there, and goes back down: an ice
    /// spike, a thorn, a fork of lightning crawling the rim.
    ///
    /// <para>It drives its own children's ALPHA rather than their colour, so a crystal built
    /// the way <c>IceWallVisual</c> builds one — an opaque body with an additive facet and rim
    /// over it — keeps the relationship between those three layers while the whole shard rises
    /// and fades. Writing a single colour over them instead would flatten the crystal into one
    /// silhouette, which is the thing the three layers exist to avoid.</para>
    ///
    /// <para>THE OVERSHOOT IS NOT DECORATION. A linear stretch from zero reads as a rectangle
    /// being scaled; something breaking a surface arrives with momentum it then loses. That is
    /// the same reason <c>RootWhipFX</c> carries <c>SPROUT_OVERSHOOT</c>.</para>
    /// </summary>
    internal sealed class AreaBurstShard : MonoBehaviour
    {
        /// <summary>Standard easeOutBack constants. The pair is one curve, not two knobs.</summary>
        private const float BACK_C1 = 1.70158f;
        private const float BACK_C3 = BACK_C1 + 1f;

        private SpriteRenderer[] _renderers;
        private Color[] _baseColors;

        private Vector3 _ground;
        private float _width, _height, _unitHeight;
        private bool _centrePivot;
        private float _lean, _swayHz, _swayPhase;
        private float _rise, _hold, _fall, _age;

        /// <param name="unitHeight">World height of the sprite at scale 1. Two for
        /// <c>IceSprites</c> shards, one for everything else — the same fact
        /// <c>IceSprites.ScaleShard</c> hides from its own callers.</param>
        /// <param name="centrePivot">True for a centre-pivoted sprite, which has to be lifted
        /// by half its drawn height or it grows down through the floor as well as up.</param>
        internal void Begin(SpriteRenderer[] renderers, float width, float height,
                            float unitHeight, bool centrePivot, float lean,
                            float rise, float hold, float fall)
        {
            _renderers = renderers;
            _baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                _baseColors[i] = renderers[i] != null ? renderers[i].color : Color.white;

            _ground = transform.position;
            _width = width;
            _height = Mathf.Max(0.05f, height);
            _unitHeight = Mathf.Max(0.01f, unitHeight);
            _centrePivot = centrePivot;
            _lean = lean;
            _swayHz = Random.Range(0.4f, 0.9f);
            _swayPhase = Random.Range(0f, Mathf.PI * 2f);
            _rise = Mathf.Max(0.01f, rise);
            _hold = Mathf.Max(0f, hold);
            _fall = Mathf.Max(0.02f, fall);

            Apply(0f, 1f);
        }

        private void Update()
        {
            _age += Time.deltaTime;

            float extension;
            float alpha;

            if (_age < _rise)
            {
                float u = _age / _rise;
                extension = EaseOutBack(u);
                alpha = 1f;
            }
            else if (_age < _rise + _hold)
            {
                extension = 1f;
                alpha = 1f;
            }
            else
            {
                float u = Mathf.Clamp01((_age - _rise - _hold) / _fall);
                // Sinks back the way it came AND fades. Either alone is wrong: a shard that
                // only fades leaves a hole in the ground it never closed, and one that only
                // sinks pops out of existence on the last frame.
                extension = 1f - u;
                alpha = 1f - u * u;
            }

            Apply(extension, alpha);

            if (_age >= _rise + _hold + _fall) Destroy(gameObject);
        }

        private void Apply(float extension, float alpha)
        {
            float drawn = _height * extension;
            transform.localScale = new Vector3(_width, drawn / _unitHeight, 1f);
            transform.position = _centrePivot
                ? _ground + new Vector3(0f, drawn * 0.5f, 0f)
                : _ground;

            float sway = Mathf.Sin((Time.time + _swayPhase) * _swayHz * Mathf.PI * 2f) * 2.5f;
            transform.localRotation = Quaternion.Euler(0f, 0f, _lean + sway);

            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Color c = _baseColors[i];
                _renderers[i].color = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(alpha));
            }
        }

        private static float EaseOutBack(float u)
        {
            float p = u - 1f;
            return 1f + BACK_C3 * p * p * p + BACK_C1 * p * p;
        }
    }
}
