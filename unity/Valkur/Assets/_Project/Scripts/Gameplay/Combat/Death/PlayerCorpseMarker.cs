using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// The body left at the death position — and, because the compass points at it, the marker
    /// the player walks back to for their loot.
    ///
    /// <para><b>It wears the player's own sprite.</b> It used to be a procedurally generated flat
    /// red 8x8 square, which was honest as a placeholder and bad as a landmark: at 16 PPU it is
    /// half a tile of solid colour, indistinguishable from a floor decal at any distance, standing
    /// over the entire contents of the player's bag. The frame the character died on is already in
    /// memory on their own <c>SpriteRenderer</c> and needs no art to be authored.</para>
    ///
    /// <para><b>Laid down rather than left standing.</b> The sprite is squashed on Y and darkened,
    /// which is what separates a corpse from the living character standing in the same place —
    /// there is no death frame for most of the roster and rotating a directional sprite reads as a
    /// bug rather than as a fall. The placeholder survives as the fallback for a player with no
    /// renderer at all, so this can never render nothing.</para>
    ///
    /// <para>Destroyed by <see cref="DeathSequenceController"/> as part of the revive, immediately
    /// or after <c>DeathTuning.corpseLingerSeconds</c>.</para>
    /// </summary>
    public class PlayerCorpseMarker : MonoBehaviour
    {
        private static Sprite s_PlaceholderSprite;

        /// <summary>Vertical squash. Enough to read as lying down without turning the sprite into a line.</summary>
        private const float LieDownSquash = 0.45f;

        /// <summary>Multiplied into the sprite. Dark enough to read as a body, light enough to identify the character.</summary>
        private static readonly Color CorpseTint = new Color(0.42f, 0.30f, 0.32f, 0.95f);

        private float _despawnAt = -1f;

        /// <summary>
        /// Spawn the body. <paramref name="player"/> is optional — when present, the corpse takes
        /// the frame the character died on.
        /// </summary>
        public static PlayerCorpseMarker Spawn(Vector3 worldPos, GameObject player = null, Transform parent = null)
        {
            var go = new GameObject("PlayerCorpse");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            if (!TryWearPlayerSprite(sr, go.transform, player)) ApplyPlaceholderSprite(sr);

            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 0;

            return go.AddComponent<PlayerCorpseMarker>();
        }

        /// <summary>
        /// Destroy after <paramref name="seconds"/> of unscaled time.
        ///
        /// <para>Driven from <c>Update</c> rather than by <c>Destroy(go, t)</c> because the corpse
        /// has to outlive a paused game the same way it outlives a slowed one — the whole death
        /// flow runs on unscaled time, and a corpse that vanished early because the player opened
        /// a menu would take the loot marker with it.</para>
        /// </summary>
        public void DespawnAfter(float seconds)
        {
            if (seconds <= 0f) { Despawn(); return; }
            _despawnAt = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            if (_despawnAt < 0f) return;
            if (Time.unscaledTime >= _despawnAt) Despawn();
        }

        public void Despawn()
        {
            if (this == null) return;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>
        /// Copy the player's live frame onto the corpse. Returns false when there is nothing to
        /// copy, which is the EditMode case and the boot-race case.
        /// </summary>
        private static bool TryWearPlayerSprite(SpriteRenderer sr, Transform corpse, GameObject player)
        {
            if (player == null) return false;

            var source = player.GetComponentInChildren<SpriteRenderer>();
            if (source == null || source.sprite == null) return false;

            sr.sprite = source.sprite;
            sr.flipX = source.flipX;
            sr.color = CorpseTint;

            // The scale is taken from the SOURCE's own lossy scale, not from 1: the roster spans
            // 1.797 to 2.667 world units and a corpse built at unit scale would be the wrong size
            // for two thirds of the characters.
            Vector3 scale = source.transform.lossyScale;
            corpse.localScale = new Vector3(scale.x, scale.y * LieDownSquash, 1f);
            return true;
        }

        private static void ApplyPlaceholderSprite(SpriteRenderer sr)
        {
            if (s_PlaceholderSprite == null)
            {
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "PlayerCorpsePlaceholder",
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[64];
                var fill = new Color(0.75f, 0.05f, 0.05f, 1f);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
                tex.SetPixels(pixels);
                tex.Apply(updateMipmaps: false);
                s_PlaceholderSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, 8, 8),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 8f);
                s_PlaceholderSprite.name = "PlayerCorpsePlaceholderSprite";
                s_PlaceholderSprite.hideFlags = HideFlags.DontSave;
            }
            sr.sprite = s_PlaceholderSprite;
            sr.color = Color.white;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload is OFF — drop the cached placeholder so a second Play session doesn't
            // reference a destroyed Texture2D.
            s_PlaceholderSprite = null;
        }
    }
}
