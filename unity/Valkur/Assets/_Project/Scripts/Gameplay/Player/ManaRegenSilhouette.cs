using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Renders a soft halo around the player's sprite silhouette while
    /// <see cref="Mana.IsRegenerating"/> is true, mirroring the trigger
    /// used by <see cref="ManaRegenAura"/> particles. Hidden the rest of
    /// the time so the visual reads cleanly as "I'm recovering mana right
    /// now" instead of an ambient hero glow.
    ///
    /// Implementation: cheapest possible "duplicate sprite, scale up, sort
    /// behind" trick. A child <see cref="SpriteRenderer"/> mirrors the
    /// source sprite each frame, sits one sorting order below the original,
    /// and is scaled slightly larger so only the rim peeks out past the
    /// silhouette. No custom shader needed; works in URP 2D regardless of
    /// the source sprite's material (Sprite-Lit-Default or Sprite-Unlit-Default).
    /// Visibility is faded in/out with <c>FadeSeconds</c> so flips between
    /// regen states feel like a soft pulse instead of a snap.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Mana))]
    public class ManaRegenSilhouette : MonoBehaviour
    {
        [SerializeField, Tooltip("Halo tint while mana is regenerating. Default is mana-blue.")]
        private Color _auraColor = new Color(0.30f, 0.55f, 1f, 0.65f);

        [SerializeField, Tooltip("Outline scale multiplier. >1 makes the duplicate extend slightly past the silhouette as a rim halo.")]
        [Range(1.00f, 1.30f)]
        private float _outlineScale = 1.08f;

        [SerializeField, Tooltip("Alpha pulse range below the configured aura alpha. 0 disables pulsing.")]
        [Range(0f, 0.5f)]
        private float _alphaPulse = 0.18f;

        [SerializeField, Tooltip("Pulse frequency in Hz.")]
        [Range(0f, 3f)]
        private float _pulseFrequencyHz = 1.1f;

        private const float FadeSeconds = 0.35f;

        private Mana _mana;
        private SpriteRenderer _source;
        private SpriteRenderer _aura;
        private Material _auraMaterial;
        private float _baseAlpha;
        private float _visibility;

        private void Awake()
        {
            _mana = GetComponent<Mana>();
            _source = GetComponentInChildren<SpriteRenderer>();
            if (_source == null) return;
            _baseAlpha = _auraColor.a;
            BuildAura();
        }

        private void OnDestroy()
        {
            if (_auraMaterial != null) Destroy(_auraMaterial);
        }

        private void LateUpdate()
        {
            if (_aura == null || _source == null) return;

            bool regen = _mana != null && _mana.IsRegenerating;
            float target = regen ? 1f : 0f;
            _visibility = Mathf.MoveTowards(_visibility, target, Time.deltaTime / Mathf.Max(0.01f, FadeSeconds));

            _aura.sprite = _source.sprite;
            _aura.flipX = _source.flipX;
            _aura.flipY = _source.flipY;

            bool sourceVisible = _source.enabled && _source.sprite != null;
            _aura.enabled = sourceVisible && _visibility > 0.001f;
            if (!_aura.enabled) return;

            float pulseAlpha = _baseAlpha;
            if (_alphaPulse > 0f)
            {
                float t = (Mathf.Sin(Time.time * _pulseFrequencyHz * Mathf.PI * 2f) + 1f) * 0.5f;
                pulseAlpha = _baseAlpha - _alphaPulse + (_alphaPulse * 2f) * t;
            }

            var c = _auraColor;
            c.a = Mathf.Clamp01(pulseAlpha * _visibility);
            _aura.color = c;
        }

        private void BuildAura()
        {
            var go = new GameObject("ManaRegenSilhouetteSprite");
            go.transform.SetParent(_source.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * _outlineScale;

            _aura = go.AddComponent<SpriteRenderer>();
            _aura.sortingLayerID = _source.sortingLayerID;
            // One rung below the body sprite so the original always covers
            // the duplicate inside the silhouette, leaving only the halo rim.
            _aura.sortingOrder = _source.sortingOrder - 1;

            // Use Sprites/Default explicitly (rather than copying the source
            // material) so the halo renders at full color regardless of
            // scene Light2D state — the cue shouldn't go dark at night.
            var shader = Shader.Find("Sprites/Default");
            _auraMaterial = new Material(shader) { name = "ManaRegenSilhouetteMaterial" };
            _aura.sharedMaterial = _auraMaterial;
            _aura.color = _auraColor;
            _aura.enabled = false;
        }
    }
}
