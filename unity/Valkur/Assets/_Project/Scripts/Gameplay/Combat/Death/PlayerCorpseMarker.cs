using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Placeholder body left at the death position. Today it's a flat red square
    /// generated at runtime so the system works without an art asset; replace the
    /// sprite later by swapping <see cref="ApplyPlaceholderSprite"/>.
    ///
    /// The corpse is destroyed when the spirit reaches the resurrection altar —
    /// <see cref="DeathSequenceController"/> tracks the current corpse and calls
    /// <see cref="Despawn"/> as part of the revive sequence.
    /// </summary>
    public class PlayerCorpseMarker : MonoBehaviour
    {
        private static Sprite s_PlaceholderSprite;

        public static PlayerCorpseMarker Spawn(Vector3 worldPos, Transform parent = null)
        {
            var go = new GameObject("PlayerCorpse");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            ApplyPlaceholderSprite(sr);
            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 0;

            // Keep the corpse on the YSort plane so it disappears under taller
            // entities. Adding YSortEntity is optional — without it the floor-decal
            // sorting layer renders below entities, which is the desired behaviour.

            return go.AddComponent<PlayerCorpseMarker>();
        }

        public void Despawn()
        {
            if (this == null) return;
            Destroy(gameObject);
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
            // Domain Reload is OFF — drop the cached placeholder so a second Play
            // session doesn't reference a destroyed Texture2D.
            s_PlaceholderSprite = null;
        }
    }
}
