using UnityEngine;
using Valkur.Gameplay.Player;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The energy (Carrera/stamina) driver. Reports a number to <see cref="WorldBarRig"/>, which
    /// draws it as its own row between health and the resource row, sharing health's outline the
    /// same way the resource row does.
    ///
    /// <para><b>Follows the <see cref="WorldDashBar"/> shape (Awake -&gt; Ensure -&gt; Enable;
    /// Update -&gt; Set) with one difference.</b> <c>Energy</c> is attached to the player by
    /// <c>EntitySetup.InitPlayerProgression</c>, which runs AFTER <c>InitSharedVisuals</c> — where
    /// this component is attached, beside <see cref="WorldDashBar"/> and <see cref="WorldManaBar"/>
    /// — so the component it drives does not exist yet the first time this one is enabled.
    /// <see cref="ResolveReferences"/> is retried every frame until it succeeds, rather than
    /// resolving once and staying inert for the rest of the session the way the dash and mana
    /// drivers may (their components are added earlier, in <c>InitPlayerCombat</c> /
    /// <c>InitPlayerStats</c>, so their single resolve always finds something).</para>
    ///
    /// <para><b>Subscribing and resolving are kept separate on purpose.</b> A disable/enable
    /// cycle (<c>UnconsciousState</c> puts this row away the same way it does the sibling
    /// drivers) must re-subscribe to the controller's events even though the component
    /// references were already resolved in an earlier session — folding "subscribe" into
    /// "resolve" would leave <see cref="OnBecameWinded"/> / <see cref="OnWindRecovered"/>
    /// permanently unhooked after the first disable.</para>
    ///
    /// <para>Player only in practice: <c>Energy</c> is added to the player alone (see its own
    /// class doc), so a monster's own resolve never finds one and this driver stays permanently
    /// idle on it — the same "no component, no row" contract every other driver here keeps.</para>
    /// </summary>
    public class WorldEnergyBar : MonoBehaviour
    {
        private Energy _energy;
        private PlayerController _controller;
        private WorldBarRig _rig;
        private int _lastEnergy = int.MinValue;
        private bool _subscribed;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            if (_rig != null) _rig.EnableEnergy(true);
        }

        private void OnDisable()
        {
            Unsubscribe();
            // UnconsciousState disables the sibling drivers on the same rule: the row goes with
            // them rather than hanging over a body that cannot run.
            _rig?.EnableEnergy(false);
        }

        private void Update()
        {
            if (_energy == null)
            {
                ResolveReferences();
                if (_energy == null) return;   // Energy still not added this frame either
                // Just became bound mid-session: this component's own OnEnable already ran and
                // found nothing, so pick up what it would have wired here instead.
                Subscribe();
                _rig.EnableEnergy(true);
            }

            var change = _energy.Current < _lastEnergy ? WorldBarChange.Damage : WorldBarChange.Silent;
            _lastEnergy = _energy.Current;
            _rig.SetEnergy(_energy.Current, _energy.Max, change);
        }

        /// <summary>Only resolves component references. No side effects — those belong to
        /// <see cref="Subscribe"/> and the caller's own <c>EnableEnergy</c> call, so both a
        /// fresh bind and a re-enable after <see cref="OnDisable"/> can reuse this safely.</summary>
        private void ResolveReferences()
        {
            if (_energy != null) return;
            _energy = GetComponent<Energy>();
            if (_energy == null) return;   // not added yet (or never will be — a monster)

            _rig = WorldBarRig.Ensure(gameObject);
            _controller = GetComponent<PlayerController>();
            _lastEnergy = _energy.Current;
        }

        private void Subscribe()
        {
            if (_subscribed || _controller == null) return;
            _controller.BecameWinded += OnBecameWinded;
            _controller.WindRecovered += OnWindRecovered;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_controller != null)
            {
                _controller.BecameWinded -= OnBecameWinded;
                _controller.WindRecovered -= OnWindRecovered;
            }
            _subscribed = false;
        }

        /// <summary>Energy hit zero: the row takes the stack's short blow-shake.</summary>
        private void OnBecameWinded() => _rig?.ShakeEnergyWinded();

        /// <summary>The wind-recovery threshold was crossed upward: the row flashes.</summary>
        private void OnWindRecovered() => _rig?.FlashEnergyRecovered();
    }
}
