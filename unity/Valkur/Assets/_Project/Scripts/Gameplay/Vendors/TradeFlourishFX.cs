using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// What a purchase looks like: a figure over the player's head and a handful of coins
    /// moving in the direction the money went.
    ///
    /// <para>Money changing hands was completely silent. The wallet updated, the row
    /// refreshed, and the only way to know a transaction had happened was to have been
    /// watching the "Gold:" line at the bottom of the shop — which is nowhere near the
    /// button just pressed, and is gone entirely once the shop closes. A trade agreed in a
    /// conversation produced no feedback whatsoever.</para>
    ///
    /// <para>DIRECTION IS THE MESSAGE. The number already says how much; what separates
    /// buying from selling at a glance is which way the coins travel — OUT of the player
    /// when they spend, IN toward them when they earn. Colour reinforces that and cannot
    /// carry it alone: a red "-15" and a gold "+15" are the same shape at the edge of
    /// vision, and roughly one man in twelve cannot separate those two hues at all.</para>
    ///
    /// <para>Nothing is parented to the player. The coins are spawned into the world and
    /// animate from where the trade happened, so walking away mid-flourish leaves them
    /// behind instead of dragging them along — which is what money in the air should do.</para>
    /// </summary>
    public static class TradeFlourishFX
    {
        /// <summary>Coins per transaction. Enough to read as "some money", few enough not to litter.</summary>
        private const int COIN_COUNT = 7;

        /// <summary>Height above the entity's pivot the flourish is centred on.</summary>
        private const float HEAD_OFFSET = 1.35f;

        private static readonly Color SpendColor = new Color(0.95f, 0.45f, 0.35f);
        private static readonly Color EarnColor = new Color(1.00f, 0.82f, 0.25f);

        /// <summary>The player just paid <paramref name="amount"/>. Coins leave them.</summary>
        public static void Spent(Transform player, int amount)
        {
            if (player == null || amount <= 0) return;
            Play(player.position, $"- {amount} g", SpendColor, outward: true);
        }

        /// <summary>The player just received <paramref name="amount"/>. Coins arrive.</summary>
        public static void Earned(Transform player, int amount)
        {
            if (player == null || amount <= 0) return;
            Play(player.position, $"+ {amount} g", EarnColor, outward: false);
        }

        private static void Play(Vector3 origin, string label, Color labelColor, bool outward)
        {
            Vector3 head = origin + Vector3.up * HEAD_OFFSET;

            // The same pooled floating text the damage numbers use. A second text system
            // here would mean a second pool, a second font and a second set of timings that
            // drift apart from the first.
            FloatingDamageSpawner.ShowAt(head, label, labelColor);

            for (int i = 0; i < COIN_COUNT; i++)
            {
                // Spread evenly around the circle rather than randomising the angle outright:
                // seven random angles clump visibly about a third of the time, and a clump
                // reads as a broken spawner rather than as a handful of coins. The jitter is
                // there so two trades in a row do not produce the same picture twice.
                float angle = (i / (float)COIN_COUNT) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                TradeCoin.Spawn(head, angle, outward);
            }
        }
    }

    /// <summary>
    /// One coin of a <see cref="TradeFlourishFX"/> burst, animating itself and cleaning up.
    ///
    /// A component per coin rather than coroutines on a shared host: a static class has no
    /// MonoBehaviour to run a coroutine on, and inventing a persistent runner to host
    /// half-second animations would outlive every effect it ever ran.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class TradeCoin : MonoBehaviour
    {
        /// <summary>How long a coin lives. Short: this is punctuation, not a cutscene.</summary>
        private const float LIFE = 0.55f;

        /// <summary>How far a coin travels, in world units.</summary>
        private const float REACH = 1.15f;

        /// <summary>Diameter of a coin. At 16 PPU that is about four screen pixels.</summary>
        private const float SIZE = 0.26f;

        /// <summary>
        /// Vertical squash on the spread. The world is drawn at an angle, so a perfect
        /// circle of coins reads as a hoop standing on its edge rather than as money
        /// scattering across the ground plane — the same reason every ground ring in this
        /// project is flattened.
        /// </summary>
        private const float GROUND_SQUASH = 0.65f;

        private static readonly Color CoinColor = new Color(1.00f, 0.84f, 0.32f);

        private SpriteRenderer _renderer;
        private Vector3 _from;
        private Vector3 _to;
        private bool _outward;
        private float _elapsed;

        internal static void Spawn(Vector3 centre, float angle, bool outward)
        {
            var go = new GameObject("TradeCoin");
            var coin = go.AddComponent<TradeCoin>();

            var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * GROUND_SQUASH, 0f) * REACH;
            coin._outward = outward;
            coin._from = outward ? centre : centre + offset;
            coin._to = outward ? centre + offset : centre;
            go.transform.position = coin._from;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = ElementalSprites.HotCore;
            renderer.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            renderer.color = CoinColor;
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 40;
            coin._renderer = renderer;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / LIFE);

            // Ease out on the way there: a coin leaves fast and settles, which is the shape
            // of something thrown rather than something on a conveyor.
            float eased = 1f - (1f - t) * (1f - t);

            // A coin arriving fades IN and a coin leaving fades OUT, so the alpha makes the
            // same statement as the direction and the two can never contradict each other.
            float alpha = _outward
                ? 1f - eased
                : Mathf.Min(1f, eased * 2f) * (1f - t * 0.35f);

            transform.position = Vector3.Lerp(_from, _to, eased)
                               + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.28f;
            transform.localScale = Vector3.one * SIZE * (0.75f + 0.25f * Mathf.Sin(t * Mathf.PI));

            if (_renderer != null)
                _renderer.color = new Color(CoinColor.r, CoinColor.g, CoinColor.b, alpha);

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
