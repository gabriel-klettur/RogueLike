using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The white hit flash. Two things made the old version invisible on NPCs:
    /// nothing ever attached <see cref="CombatFeedback"/> to a monster, and the
    /// flash worked by setting <c>SpriteRenderer.color</c> to white — which is a
    /// multiply, so on the white-tinted sprites <c>EntityAnimationBinder</c>
    /// produces it changed literally nothing.
    ///
    /// The flash now drives a <c>_FlashAmount</c> uniform on
    /// <c>Valkur/SpriteHDRTint</c> through a MaterialPropertyBlock: the shader
    /// lerps the fragment toward the flash colour, so the sprite whites out
    /// regardless of its base tint, and no per-entity material is allocated.
    /// Renderers whose material predates that uniform fall back to the old colour
    /// tint rather than silently doing nothing.
    ///
    /// Driven from Update rather than a coroutine so it is deterministic, cannot
    /// be orphaned by a StopCoroutine race, and can be stepped by EditMode tests.
    /// </summary>
    public partial class CombatFeedback
    {
        [Header("Hit Flash")]
        [SerializeField, Tooltip("Total length of the flash in seconds.")]
        private float flashDuration = 0.14f;

        [SerializeField, Range(0f, 0.95f),
         Tooltip("Fraction of the flash held at full strength before it ramps back down. " +
                 "A short hold reads as an impact; a pure ramp reads as a glow.")]
        private float flashHold = 0.35f;

        [SerializeField, Tooltip("Colour the sprite is pushed toward while flashing.")]
        private Color flashColor = Color.white;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorId  = Shader.PropertyToID("_FlashColor");

        // Only the entity's own body sprite flashes. The world-space health, mana
        // and dash bars hang off the same transform as child SpriteRenderers —
        // whitening those would blank the HP bar on every hit.
        private SpriteRenderer _bodyRenderer;
        private Color _bodyBaseColor = Color.white;
        private bool _bodyFlashesViaShader;
        private MaterialPropertyBlock _flashMpb;
        private float _flashTimer;
        private float _appliedFlash = -1f;

        /// <summary>
        /// Current flash strength: 0 when the sprite is untouched, 1 at full white.
        /// Public so tests can assert the flash actually happened.
        /// </summary>
        public float FlashAmount { get; private set; }

        /// <summary>True while the flash is still playing.</summary>
        public bool IsFlashing => _flashTimer > 0f;

        /// <summary>
        /// True when the body sprite can flash through the shader uniform. False
        /// means it fell back to tinting SpriteRenderer.color, which cannot
        /// brighten an already-white sprite.
        /// </summary>
        public bool UsesShaderFlash
        {
            get
            {
                EnsureHitFlashReady();
                return _bodyFlashesViaShader;
            }
        }

        /// <summary>
        /// Resolve the body sprite and re-read whether it can flash through the
        /// shader. Idempotent, and safe to call again after a pooled entity swaps
        /// its visuals or its material.
        /// </summary>
        public void EnsureHitFlashReady()
        {
            _flashMpb ??= new MaterialPropertyBlock();

            if (_bodyRenderer == null)
            {
                // Same body-sprite rule the rest of the entity code uses
                // (EntitySpriteHelper, GrayscaleDeath): the renderer on this object,
                // or the first one below it.
                _bodyRenderer = GetComponent<SpriteRenderer>();
                if (_bodyRenderer == null) _bodyRenderer = GetComponentInChildren<SpriteRenderer>();
                if (_bodyRenderer != null) _bodyBaseColor = _bodyRenderer.color;
            }

            RefreshFlashCapability();
        }

        /// <summary>
        /// Re-read whether the body material can flash through the shader.
        /// This cannot be decided once and cached: on a prefab-spawned monster
        /// Awake runs during Instantiate, before EntitySetup.ConfigureMonster swaps
        /// in the HDR sprite material — freezing the answer there left every NPC
        /// permanently on the fallback path, writing a white tint onto an
        /// already-white sprite. Which is to say: not flashing at all.
        /// </summary>
        private void RefreshFlashCapability()
        {
            if (_bodyRenderer == null) { _bodyFlashesViaShader = false; return; }

            var material = _bodyRenderer.sharedMaterial;
            _bodyFlashesViaShader = material != null && material.HasProperty(FlashAmountId);
        }

        /// <summary>Start (or restart) the flash. Retriggering resets it to full.</summary>
        public void TriggerHitFlash()
        {
            EnsureHitFlashReady();
            if (_bodyRenderer == null) return;
            if (flashDuration <= 0f) return;

            // Re-read the resting colours only when nothing is currently applied, so
            // a second hit mid-flash does not capture the flashed colour as the
            // colour to restore.
            if (FlashAmount <= 0f) CaptureBaseColors();

            _flashTimer = flashDuration;
            ApplyFlash(1f);
        }

        /// <summary>
        /// Advance the flash. Public so EditMode tests can step it without a
        /// coroutine or a running player loop.
        /// </summary>
        public void TickHitFlash(float deltaTime)
        {
            if (_flashTimer <= 0f) return;

            _flashTimer = Mathf.Max(0f, _flashTimer - deltaTime);

            if (_flashTimer <= 0f)
            {
                ApplyFlash(0f);
                return;
            }

            // Hold at full strength for the first slice, then ramp down. The hold is
            // what makes a hit read as an impact instead of a soft glow.
            float remaining = flashDuration > 0f ? _flashTimer / flashDuration : 0f;
            float rampSpan = 1f - flashHold;
            float amount = rampSpan <= 0f || remaining >= rampSpan
                ? 1f
                : remaining / rampSpan;

            ApplyFlash(amount);
        }

        /// <summary>Stop the flash immediately and restore the sprite.</summary>
        private void CancelHitFlash()
        {
            _flashTimer = 0f;
            if (FlashAmount > 0f || _appliedFlash > 0f) ApplyFlash(0f);
        }

        private void CaptureBaseColors()
        {
            if (_bodyRenderer != null) _bodyBaseColor = _bodyRenderer.color;
        }

        private void ApplyFlash(float amount)
        {
            FlashAmount = amount;
            if (Mathf.Approximately(_appliedFlash, amount)) return;
            _appliedFlash = amount;

            if (_bodyRenderer == null) return;

            if (_bodyFlashesViaShader)
            {
                // GetPropertyBlock first so the per-entity HDR tint that
                // EntityAnimationBinder wrote into the same block survives.
                _bodyRenderer.GetPropertyBlock(_flashMpb);
                _flashMpb.SetFloat(FlashAmountId, amount);
                _flashMpb.SetColor(FlashColorId, flashColor);
                _bodyRenderer.SetPropertyBlock(_flashMpb);
            }
            else
            {
                // Legacy path: a multiply, so it only shows on sprites whose
                // resting colour is not already the flash colour.
                _bodyRenderer.color = Color.Lerp(_bodyBaseColor, flashColor, amount);
            }
        }
    }
}
