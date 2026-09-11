using UnityEngine;
using Valkur.Core.UI;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The health driver: it reports a number to <see cref="WorldBarRig"/> and owns no pixels.
    ///
    /// <para>It kept its name and its whole public surface because six systems attach or address
    /// it — <c>EntitySetup</c> twice, <c>AlliedSummonService</c>, <c>NPCRespawnSystem</c>,
    /// <c>UnconsciousState</c>, and both <c>InteractionPromptView</c> and <c>HarvestNodeBar</c>
    /// for its shared sprite and material — but everything it used to DO now lives in the rig.
    /// The split is what stops the mana and dash bars re-deriving this bar's geometry from
    /// private copies of its constants, which is what they did.</para>
    ///
    /// <para><b>It distinguishes a blow from a heal, and that is new.</b> The old bar subscribed
    /// to <c>OnHpChanged</c> alone, so it could see that a number had moved and never why: no
    /// chip, no flash, no shake, and a heal that looked exactly like a hit. <c>OnDamaged</c> fires
    /// immediately before <c>OnHpChanged</c> inside <c>Health.TakeDamage</c>, which is what makes
    /// the distinction free.</para>
    /// </summary>
    public class WorldHealthBar : MonoBehaviour
    {
        private Health _health;
        private WorldBarRig _rig;
        private int _lastHp = int.MinValue;
        private bool _blowPending;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _rig = WorldBarRig.Ensure(gameObject);
            _rig.SetRank(ResolveRank());
        }

        private void OnEnable()
        {
            if (_rig == null) return;
            _rig.SetSuppressed(false);
            if (_health == null) return;

            _health.OnHpChanged += OnHpChanged;
            _health.OnDamaged += OnDamaged;
            _lastHp = int.MinValue;
            OnHpChanged(_health.CurrentHp, _health.MaxHp);
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.OnHpChanged -= OnHpChanged;
                _health.OnDamaged -= OnDamaged;
            }
            // UnconsciousState disables this component to put a downed NPC's readout away. The
            // rig is a separate object, so saying so explicitly is the only thing that hides it.
            if (_rig != null) _rig.SetSuppressed(true);
        }

        private void OnDamaged(int amount) => _blowPending = amount > 0;

        private void OnHpChanged(int current, int max)
        {
            if (_rig == null) return;

            WorldBarChange change;
            if (_blowPending) change = WorldBarChange.Damage;
            else if (_lastHp != int.MinValue && current > _lastHp) change = WorldBarChange.Heal;
            else change = WorldBarChange.Silent;

            _blowPending = false;
            _lastHp = current;
            _rig.SetHealth(current, max, change);
        }

        /// <summary>
        /// What the frame says about this creature. Player and ally are answered here because
        /// both are properties of the object itself; elite and boss come from the
        /// <c>MonsterDefinition</c>, which only <c>EntitySetup</c> holds, through
        /// <see cref="SetRank"/>.
        /// </summary>
        private WorldBarRank ResolveRank()
        {
            if (CompareTag("Player")) return WorldBarRank.Player;
            if (AlliedUnit.IsAllied(gameObject)) return WorldBarRank.Ally;
            return WorldBarRank.Normal;
        }

        /// <summary>Set the frame's rank explicitly. Player and Ally are resolved without it.</summary>
        public void SetRank(WorldBarRank rank)
        {
            if (_rig == null) _rig = WorldBarRig.Ensure(gameObject);
            _rig.SetRank(rank);
        }

        /// <summary>
        /// Override the health colours for this entity.
        ///
        /// <para>Kept for compatibility and deliberately no longer called by the shipped setup:
        /// the palette lives in <c>WorldBarStyle</c> now, and three call sites each passing their
        /// own literals is what made the asset unable to change anything.</para>
        /// </summary>
        public void SetBarColors(Color fill, Color low)
        {
            if (_rig == null) _rig = WorldBarRig.Ensure(gameObject);
            _rig.SetHealthColours(fill, low);
        }

        /// <summary>Whether this bar may fade away when it has nothing to report.</summary>
        public void SetHideAtFullHp(bool hide)
        {
            if (_rig == null) _rig = WorldBarRig.Ensure(gameObject);
            _rig.SetHideAtFullHealth(hide);
        }

        /// <summary>Re-measure the body. Call after a loadout swap or a scale change.</summary>
        public void Remeasure() => _rig?.Remeasure();

        /// <summary>
        /// Local-space Y of the top of an entity's sprite, above its pivot. Kept public because
        /// it predates the rig and reads correctly for any caller that wants to hang something
        /// over a creature's head.
        /// </summary>
        public static float GetSpriteTopY(GameObject entity)
        {
            var sr = entity.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                float worldTopY = sr.bounds.max.y - entity.transform.position.y;
                float scaleY = entity.transform.localScale.y;
                return scaleY > 0f ? worldTopY / scaleY : worldTopY;
            }
            return 1.4f; // fallback for a ~22px tall sprite at PPU 16
        }

        // -- Shared white pixel, kept for the two callers outside this subsystem --------------
        // InteractionPromptView and HarvestNodeBar both build their own plates from these. They
        // are NOT what the bars draw with any more; the bars use WorldBarArt's atlas.

        private static Sprite _sharedPixelSprite;
        private static Material _sharedMaterial;

        /// <summary>A 4x4 white sprite. Shared by the interaction prompt and the harvest bar.</summary>
        public static Sprite GetSharedPixelSprite()
        {
            if (_sharedPixelSprite != null) return _sharedPixelSprite;

            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            _sharedPixelSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _sharedPixelSprite;
        }

        /// <summary>The shared unlit material those same two callers point at.</summary>
        public static Material GetSharedSpriteMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;
            _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            return _sharedMaterial;
        }
    }
}
