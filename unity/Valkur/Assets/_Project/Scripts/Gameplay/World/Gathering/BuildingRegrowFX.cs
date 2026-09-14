using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A stump that regrows GROWS: the tree comes back small and rises to its full size with a
    /// little overshoot, shedding a few leaves.
    ///
    /// <para>Only when somebody can see it. A regrow whose deadline passed while the player was
    /// away is applied the frame the world loads, and a forest of trees simultaneously springing
    /// up around the spawn point reads as a rendering fault rather than as time having passed.</para>
    ///
    /// <para><b>THE REST SCALE IS READ, NEVER REMEMBERED.</b> It is whatever
    /// <c>RestorePristine</c> just put back, captured the moment the animation starts, and
    /// written back exactly when it ends — so an instance with a scale override cannot come back
    /// at the template's size.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingRegrowFX : MonoBehaviour
    {
        private const float DURATION = 1.4f;
        private const float START_SCALE = 0.2f;

        private Vector3 _rest;
        private float _t = DURATION;

        public bool IsGrowing => _t < DURATION;

        public static BuildingRegrowFX Play(BuildingObject building, DestructionProfile profile)
        {
            if (building == null) return null;

            var footprint = building.FootprintRenderer;
            if (footprint == null || !footprint.isVisible) return null;

            var fx = building.GetComponent<BuildingRegrowFX>();
            if (fx == null) fx = building.gameObject.AddComponent<BuildingRegrowFX>();
            fx.Begin(profile);
            return fx;
        }

        private void Begin(DestructionProfile profile)
        {
            if (!IsGrowing) _rest = transform.localScale;
            _t = 0f;
            transform.localScale = _rest * START_SCALE;
            enabled = true;

            Color leaf = profile != null ? profile.leafColor : new Color(0.36f, 0.58f, 0.25f, 1f);
            HarvestFx.Leaves(transform.position + Vector3.up * 0.4f, leaf, 12, 0.6f);
        }

        private void LateUpdate()
        {
            if (!IsGrowing) { enabled = false; return; }

            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / DURATION);

            // Ease-out-back: overshoots by ~8 % and settles, which is what reads as growth rather
            // than as a sprite being scaled.
            const float c1 = 1.4f;
            const float c3 = c1 + 1f;
            float e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
            float s = Mathf.LerpUnclamped(START_SCALE, 1f, e);

            transform.localScale = _rest * s;
            if (k >= 1f) Finish();
        }

        public void Finish()
        {
            _t = DURATION;
            transform.localScale = _rest;
            enabled = false;
        }

        private void OnDisable()
        {
            if (IsGrowing) Finish();
        }
    }
}
