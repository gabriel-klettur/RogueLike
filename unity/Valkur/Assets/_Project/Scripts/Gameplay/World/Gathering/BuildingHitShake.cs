using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A struck tree SHUDDERS: the trunk jolts away from the axe and the crown lags and sways
    /// back, over a quarter of a second.
    ///
    /// <para><b>WHY THE THING BEING STRUCK HAS TO MOVE.</b> Chips, a flash and a camera nudge are
    /// all things that happen AROUND a tree. Without the tree itself reacting, the eye reads a
    /// character hitting a painting — the only object that does not care is the one being hit.
    /// The crown moves further and later than the trunk because that is what a trunk that took
    /// the blow at its base does, and a single rigid jolt reads as the sprite being nudged.</para>
    ///
    /// <para><b>OFFSETS, NEVER POSITIONS.</b> The two halves' resting local positions are captured
    /// on the first kick and put back exactly when the shake ends. Nothing else writes those
    /// transforms — the wind sway is a vertex shader — so the capture cannot go stale, and a
    /// kick that arrives mid-shake restarts the envelope rather than stacking a second offset on
    /// top of the first.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingHitShake : MonoBehaviour
    {
        private const float DURATION = 0.28f;
        private const float FREQUENCY = 26f;

        /// <summary>World units the trunk moves at the peak of a full-strength blow.</summary>
        private const float TRUNK_AMPLITUDE = 0.035f;

        /// <summary>The crown moves further than the base that was struck.</summary>
        private const float CROWN_AMPLITUDE = 0.085f;

        private BuildingObject _building;
        private Transform _trunk, _crown;
        private Vector3 _trunkRest, _crownRest;
        private bool _captured;
        private float _t = DURATION;
        private float _sign = 1f;
        private float _strength = 1f;

        public bool IsShaking => _t < DURATION;

        public static BuildingHitShake Kick(BuildingObject building, float directionSign, float strength)
        {
            if (building == null) return null;
            var shake = building.GetComponent<BuildingHitShake>();
            if (shake == null) shake = building.gameObject.AddComponent<BuildingHitShake>();
            shake.Begin(building, directionSign, strength);
            return shake;
        }

        private void Begin(BuildingObject building, float directionSign, float strength)
        {
            _building = building;
            if (!_captured) Capture();
            _sign = directionSign >= 0f ? 1f : -1f;
            _strength = Mathf.Clamp(strength, 0.2f, 1.6f);
            _t = 0f;
            enabled = true;
        }

        private void Capture()
        {
            _trunk = _building.FootprintRenderer != null ? _building.FootprintRenderer.transform : null;
            _crown = _building.CanopyRenderer != null ? _building.CanopyRenderer.transform : null;
            if (_trunk != null) _trunkRest = _trunk.localPosition;
            if (_crown != null) _crownRest = _crown.localPosition;
            _captured = true;
        }

        private void LateUpdate()
        {
            if (_t >= DURATION) { enabled = false; return; }

            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / DURATION);
            float envelope = (1f - k) * (1f - k);

            // Trunk: one sharp jolt that settles. Crown: the same wave a beat later.
            float trunk = Mathf.Sin(_t * FREQUENCY) * envelope * TRUNK_AMPLITUDE * _strength * _sign;
            float crown = Mathf.Sin((_t - 0.035f) * FREQUENCY * 0.7f) * envelope * CROWN_AMPLITUDE * _strength * _sign;

            // The parent scale would multiply a world-unit amplitude, so divide it back out.
            float sx = Mathf.Abs(transform.lossyScale.x) > 0.0001f ? transform.lossyScale.x : 1f;

            if (_trunk != null) _trunk.localPosition = _trunkRest + new Vector3(trunk / sx, 0f, 0f);
            if (_crown != null) _crown.localPosition = _crownRest + new Vector3(crown / sx, 0f, 0f);

            if (_t >= DURATION) Settle();
        }

        /// <summary>Put both halves exactly back. Also called when the building is felled mid-shake.</summary>
        public void Settle()
        {
            _t = DURATION;
            if (_trunk != null) _trunk.localPosition = _trunkRest;
            if (_crown != null) _crown.localPosition = _crownRest;
            enabled = false;
        }

        private void OnDisable()
        {
            if (_captured && _t < DURATION) Settle();
        }
    }
}
