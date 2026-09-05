using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// What a summon looks like for the whole of its service, as opposed to its entrance
    /// (<see cref="SummonRiseFX"/>) or its dismissal (<c>AllyDismissFX</c>).
    ///
    /// <para>WHY A RIM AT ALL. A summon that is indistinguishable from an enemy is a UI
    /// failure wearing a VFX costume: in a fight the player has to decide in a fraction of a
    /// second whether the thing in front of them is theirs, and a green minimap dot answers
    /// that on a different part of the screen. The rim answers it where they are looking.</para>
    ///
    /// <para>IT MUST BE REBASED EVERY FRAME. <c>YSortEntity</c> rewrites an entity's
    /// <c>sortingOrder</c> whenever it walks, so a value captured once at build time pops the
    /// rim in front of — or behind — the creature the first time it moves. Same measurement
    /// <c>KiAuraFX</c> and <c>ShieldSphereFX</c> record for their own layers. The sprite is
    /// re-read too, because a <c>DirectionalAnimator</c> swaps it several times a second and a
    /// rim holding the idle frame while the body runs is worse than no rim.</para>
    ///
    /// <para>THIS CLASS DOES NOT OWN THE LIFETIME. <c>AlliedUnit</c> does — it raises
    /// <c>OnExpired</c>, which <c>AlliedSummonService</c> has already wired to
    /// <c>AllyDismissFX</c>. Duplicating the clock here is how the rim would finish fading two
    /// seconds after the creature had gone.</para>
    /// </summary>
    internal sealed class SummonController : MonoBehaviour
    {
        /// <summary>Seconds before expiry that the rim starts to cool and the leaves start.</summary>
        private const float FAREWELL_SECONDS = 2f;

        private const float RIM_SWELL = 1.09f;
        private const int LEAF_COUNT = 10;
        private const int ORDER_LEAF = 47;

        /// <summary>Rest alpha of the rim. Deliberately modest: it has to be readable at a
        /// glance without competing with the creature's own art.</summary>
        private const float RIM_ALPHA = 0.55f;

        private RootPalette _palette;
        private float _fallbackLifetime;
        private float _age;

        private AlliedUnit _ally;
        private SpriteRenderer _body;
        private SpriteRenderer _rim;

        private SpriteRenderer[] _leaves;
        private Vector3[] _leafDrift;
        private bool _leavesBuilt;

        internal void Initialize(RootPalette palette, float lifetime)
        {
            _palette = palette;
            _fallbackLifetime = lifetime;
            _ally = GetComponent<AlliedUnit>();
            _body = ResolveBodyRenderer();
            BuildRim();
        }

        private SpriteRenderer ResolveBodyRenderer()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
        }

        private void BuildRim()
        {
            if (_body == null) return;
            ElementalSprites.EnsureAll();

            var go = new GameObject("AllySummonRim");
            // Parented to the BODY renderer's transform so the entity's own scale and any
            // animation offset are inherited for free. Nothing with a Light2D hangs under it,
            // so L6's "never scale a root that sized its children" does not bite here.
            go.transform.SetParent(_body.transform, false);
            go.transform.localScale = Vector3.one * RIM_SWELL;

            _rim = go.AddComponent<SpriteRenderer>();
            _rim.sprite = _body.sprite;
            _rim.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            _rim.color = WithAlpha(_palette.Leaf, 0f);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_rim == null || _body == null) return;

            float remaining = _ally != null && _ally.RemainingSeconds >= 0f
                ? _ally.RemainingSeconds
                : Mathf.Max(0f, _fallbackLifetime - _age);

            SyncRim();

            // The swell of the rim is a slow breath rather than a flicker: a summon is a
            // steady state, and an event-rate pulse on something that lives twenty seconds
            // stops being read within the first two.
            float breath = 0.86f + 0.14f * Mathf.Sin(Time.time * 2.4f);
            float farewell = Mathf.Clamp01(remaining / FAREWELL_SECONDS);
            _rim.color = WithAlpha(_palette.Leaf, RIM_ALPHA * breath * farewell);

            if (remaining <= FAREWELL_SECONDS) UpdateFarewell(farewell);
        }

        /// <summary>
        /// Re-read everything the animator and the Y-sorter own. Cheap — three field reads
        /// and two assignments — and the alternative is a rim that is correct for one frame.
        /// </summary>
        private void SyncRim()
        {
            _rim.sprite = _body.sprite;
            _rim.flipX = _body.flipX;
            _rim.flipY = _body.flipY;
            _rim.sortingLayerID = _body.sortingLayerID;
            // One BELOW the body, so what shows is the fringe around the silhouette rather
            // than a wash over the art.
            _rim.sortingOrder = _body.sortingOrder - 1;
        }

        /// <summary>
        /// The last two seconds. Leaves come off the creature and drift up as the rim cools,
        /// so the player is told the summon is about to go while there is still time to act
        /// on it. The SINKING itself belongs to <c>AllyDismissFX</c>, fired by
        /// <c>AlliedUnit.OnExpired</c>.
        /// </summary>
        private void UpdateFarewell(float farewell)
        {
            if (!_leavesBuilt) BuildLeaves();
            if (_leaves == null) return;

            for (int i = 0; i < _leaves.Length; i++)
            {
                if (_leaves[i] == null) continue;
                _leaves[i].transform.position += _leafDrift[i] * Time.deltaTime;
                _leaves[i].transform.localRotation *= Quaternion.Euler(0f, 0f, 60f * Time.deltaTime);
                _leaves[i].color = WithAlpha(_palette.Leaf, (1f - farewell) * 0.6f * farewell * 4f);
            }
        }

        private void BuildLeaves()
        {
            _leavesBuilt = true;
            _leaves = new SpriteRenderer[LEAF_COUNT];
            _leafDrift = new Vector3[LEAF_COUNT];

            for (int i = 0; i < LEAF_COUNT; i++)
            {
                var go = new GameObject($"Leaf{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(Random.Range(-0.45f, 0.45f),
                                                         Random.Range(0.1f, 1.1f), 0f);
                go.transform.localScale = Vector3.one * Random.Range(0.10f, 0.20f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.Wisp;
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_LEAF;
                sr.color = WithAlpha(_palette.Leaf, 0f);

                _leaves[i] = sr;
                _leafDrift[i] = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.35f, 0.9f), 0f);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
