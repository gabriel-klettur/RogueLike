using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Running out, and coming back: the spent state, the regrow clock, and the two halves of a
    /// Destroy-mode node's life that <see cref="BuildingDurability"/> owns and this side mirrors.
    /// </summary>
    public partial class HarvestNode
    {
        private SpriteRenderer[] _tintedRenderers = Array.Empty<SpriteRenderer>();
        private Color[] _pristineColors = Array.Empty<Color>();

        /// <summary>
        /// When this node refills, as a Unix timestamp. 0 while it still has charges, or when its
        /// profile names no regrow. What the save layer records.
        /// </summary>
        public double RegrowAtUnix => _regrowAtUnix;

        /// <summary>
        /// Restore a partially worked node, for the save layer. Clamped rather than trusted: a
        /// profile rebalanced downward since the run was saved would otherwise leave a node
        /// holding more than a full one.
        /// </summary>
        public void RestoreCharges(int value)
        {
            if (_profile == null) return;

            _chargesRemaining = Mathf.Clamp(value, 0, Mathf.Max(1, _profile.charges));
            if (_chargesRemaining <= 0 && !_spent) EnterSpentState();
        }

        /// <summary>
        /// Put a spent node back exactly as a previous session left it, INCLUDING when it is due
        /// to refill. <see cref="RestoreCharges"/> alone cannot express it: entering the spent
        /// state computes a FRESH deadline, so a seam emptied five minutes before the player quit
        /// would come back with its full timer running again on every load.
        /// </summary>
        public void RestoreSpent(int charges, double regrowAtUnix)
        {
            RestoreCharges(charges);
            if (_spent) _regrowAtUnix = regrowAtUnix;
            RefreshTicking();
        }

        /// <summary>
        /// Mark the node worked out. In Deplete mode the building SURVIVES, tinted so it reads as
        /// exhausted — the whole difference between a seam and a crate.
        /// </summary>
        private void EnterSpentState()
        {
            if (_spent) return;
            _spent = true;
            _chargesRemaining = 0;

            Depleted?.Invoke();

            if (_profile != null && _profile.harvestMode == HarvestMode.Deplete)
                ApplySpentTint(true);

            _regrowAtUnix = _profile != null && _profile.regrowSeconds > 0f
                ? WorldDamageService.UnixNow() + _profile.regrowSeconds
                : 0d;

            RefreshTicking();
        }

        /// <summary>
        /// Refill a spent node once its deadline passes. The deadline is CLEARED before the state
        /// flips: a listener on <see cref="Regrown"/> writes straight back into this node, and one
        /// that re-entered a still-spent node would arm a second deadline — measured live, a mine
        /// regrew and was immediately spent again, over and over.
        /// </summary>
        private void TickRegrow()
        {
            if (!_spent) return;
            if (_profile == null || _profile.harvestMode != HarvestMode.Deplete) return;
            if (_regrowAtUnix <= 0d) return;
            if (WorldDamageService.UnixNow() < _regrowAtUnix) return;

            _regrowAtUnix = 0d;
            _spent = false;
            _chargesRemaining = Mathf.Max(1, _profile.charges);
            ApplySpentTint(false);
            RefreshTicking();
            Regrown?.Invoke();
        }

        /// <summary>
        /// A Destroy-mode node is gone the moment its durability runs out. It pays the fell bonus
        /// to whoever finished it and LEAVES the registry, which is walked every frame the player
        /// is alive — a felled forest that stayed in it would be paid for forever.
        /// </summary>
        private void OnBuildingDestroyed(Vector2 contactPoint, DamageClass damageClass)
        {
            CancelInteraction();
            if (_spent) return;

            PayFellBonus();

            _spent = true;
            _chargesRemaining = 0;
            _workTowardYield = 0;
            RefreshTicking();
            Depleted?.Invoke();

            if (!_registered) return;
            InteractableRegistry.Unregister(this);
            _registered = false;
        }

        /// <summary>
        /// The felled building came back. Owning the regrow is <see cref="BuildingDurability"/>'s
        /// job (restoring a felled building re-applies the pristine snapshot); this side only has
        /// to re-enter the registry it left.
        /// </summary>
        private void OnBuildingRegrown()
        {
            _spent = false;
            _regrowAtUnix = 0d;
            _workTowardYield = 0;
            _chargesRemaining = _profile != null ? Mathf.Max(1, _profile.charges) : 0;
            RefreshTicking();

            if (_registered || _profile == null || !_profile.harvestable) return;
            InteractableRegistry.Register(this);
            _registered = true;

            Regrown?.Invoke();
        }

        /// <summary>
        /// Multiply the spent colour over the node, remembering what was there first. Captured
        /// ONCE: capturing on every call would record the spent colour as the baseline the second
        /// time round and stain the node permanently.
        /// </summary>
        private void ApplySpentTint(bool spent)
        {
            if (_tintedRenderers.Length == 0) CachePristineColors();

            for (int i = 0; i < _tintedRenderers.Length; i++)
            {
                var sr = _tintedRenderers[i];
                if (sr == null) continue;

                sr.color = spent && _profile != null
                    ? _pristineColors[i] * _profile.spentTint
                    : _pristineColors[i];
            }
        }

        private void CachePristineColors()
        {
            _tintedRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            _pristineColors = new Color[_tintedRenderers.Length];

            for (int i = 0; i < _tintedRenderers.Length; i++)
                _pristineColors[i] = _tintedRenderers[i] != null
                    ? _tintedRenderers[i].color
                    : Color.white;
        }
    }
}
