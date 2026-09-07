using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The single owner of "put coins on the ground".
    ///
    /// <para>It exists because there are now two callers with the same need and opposite
    /// reasons — <c>PlayerDeathDropSystem</c> spilling a purse the player already earned, and
    /// <c>DeathDropSystem</c> minting a monster's reward — and a second copy of the shell
    /// (layer, sprite, collider, sorting) is the shape this project has paid for before.
    /// The placeholder sprite in particular is a static texture with a Domain-Reload reset
    /// hook: two of those is two textures leaking past a recompile, not one.</para>
    ///
    /// <para>Piles rather than one pickup per coin: <see cref="Spill"/> splits a total into
    /// chunks of <see cref="ChunkSize"/> so a 300-coin purse is a dozen collectable stacks
    /// instead of three hundred colliders. A monster reward is normally under one chunk and
    /// comes out as a single pickup, which is why <see cref="SpawnPile"/> is public too.</para>
    /// </summary>
    public static class CoinDropSpawner
    {
        /// <summary>Largest number of coins one ground pickup may carry.</summary>
        public const int ChunkSize = 25;

        /// <summary>
        /// Spawns one pickup worth <paramref name="amount"/> coins at <paramref name="position"/>.
        /// Amounts at or below zero spawn nothing and return null — a monster whose reward
        /// rounded away must leave no invisible pickup behind.
        /// </summary>
        public static CoinPickup SpawnPile(int amount, Vector3 position)
        {
            if (amount <= 0) return null;

            var go = new GameObject($"CoinDrop_{amount}");
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            if (pickupLayer >= 0) go.layer = pickupLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CoinSpriteFactory.GetOrCreate();
            sr.color = new Color(1f, 0.85f, 0.25f, 1f);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;

            go.AddComponent<CircleCollider2D>(); // CoinPickup's RequireComponent
            var coin = go.AddComponent<CoinPickup>();
            coin.Initialize(amount, position);
            return coin;
        }

        /// <summary>
        /// Scatters <paramref name="total"/> coins around <paramref name="origin"/> as one or
        /// more piles and returns how many piles were made. Scatter matters for the same
        /// reason it does for item drops: several stacks on one point read as a single pickup
        /// and the player walks away from the rest.
        /// </summary>
        public static int Spill(int total, Vector3 origin, float scatterRadius)
        {
            if (total <= 0) return 0;

            int spawned = 0;
            int remaining = total;
            while (remaining > 0)
            {
                int chunk = Mathf.Min(ChunkSize, remaining);
                remaining -= chunk;

                Vector2 offset = Random.insideUnitCircle * scatterRadius;
                SpawnPile(chunk, origin + new Vector3(offset.x, offset.y, 0f));
                spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// Placeholder coin art, generated once. Kept private to this class so there is
        /// exactly one texture and one reset hook for it in the domain.
        /// </summary>
        private static class CoinSpriteFactory
        {
            private static Sprite s_Sprite;

            public static Sprite GetOrCreate()
            {
                if (s_Sprite != null) return s_Sprite;
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "CoinDropPlaceholder",
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[64];
                Color gold = new Color(1f, 0.85f, 0.2f, 1f);
                Color empty = new Color(0f, 0f, 0f, 0f);
                // Round-ish disc inside an 8x8 grid.
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    float dx = x - 3.5f;
                    float dy = y - 3.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * 8 + x] = r <= 3.5f ? gold : empty;
                }
                tex.SetPixels(pixels);
                tex.Apply(false);
                s_Sprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 16f);
                s_Sprite.name = "CoinDropPlaceholderSprite";
                s_Sprite.hideFlags = HideFlags.DontSave;
                return s_Sprite;
            }

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics() => s_Sprite = null;
        }
    }
}
