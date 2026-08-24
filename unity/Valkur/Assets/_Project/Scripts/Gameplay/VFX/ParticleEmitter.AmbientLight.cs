using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {
        // ------------------------------------------------------------------ ambient light
        //
        // Every particle material is built on an unlit shader (see ParticleMaterialCache), so
        // the URP 2D Global Light 2D that DayNightCycle drives never reaches these quads: at
        // deep night the tilemap underneath falls to a few percent brightness while the leaves
        // on top of it keep rendering at noon values. A preset that sets
        // ParticleVfxParams.respondsToAmbientLight gets the cycle's colour folded into its
        // START colour instead — the one point that composes with both gradient paths, since
        // colorOverLifetime MULTIPLIES the start colour and therefore BuildGradientFromCurves
        // and BuildFadeOutGradient inherit the tint without either of them knowing the
        // day/night cycle exists. RGB only: alpha is coverage, not brightness.

        /// <summary>
        /// Per-channel floor on the ambient multiplier. DayNightCycle's night keyframe is
        /// (0.20, 0.25, 0.45) today, but it is live-editable from the Lighting editor
        /// (Ctrl+F3) and the Time/Weather editor, so a designer can drag it to literal black
        /// — which would multiply the vegetation out of existence rather than dim it, and a
        /// leaf that stops rendering reads as a bug, not as night. The pixel-art foliage sits
        /// around 0.6 luminance, so below roughly 0.15 it stops separating from the ground it
        /// falls over. 0.25 is about two stops under daylight: unmistakably night, still there.
        /// </summary>
        private const float AmbientChannelFloor = 0.25f;

        /// <summary>
        /// Seconds between ambient re-reads. The tint only MOVES during the Dawn and Dusk
        /// bands (Day and Night are flat keyframes, not ramps), which are 0.12 of the cycle
        /// each — 432 real seconds apiece at the shipped 3600 s day — so 0.5 s samples a ramp
        /// roughly 860 times and the per-tick colour step is under 0.2% of the whole
        /// night-to-day swing. It also bounds how long a clock scrub in the Lighting editor
        /// takes to reach the particles.
        /// </summary>
        private const float AmbientTickSeconds = 0.5f;

        /// <summary>
        /// Below this the ambient has not meaningfully moved and the systems are left alone.
        /// This is what makes the steady state free: the cycle's colour is constant through
        /// the entire Day band and the entire Night band — ~76% of the day — during which a
        /// tick costs three float compares and touches no ParticleSystem at all.
        /// </summary>
        private const float AmbientEpsilon = 0.004f;

        /// <summary>
        /// One ParticleSystem that opted in, paired with the params block the tint loop
        /// re-runs <see cref="BuildColorParameter"/> against. Captured rather than re-derived
        /// because <see cref="_layerSystems"/> is indexed by VALID layer, not by authored
        /// layer, so mapping a system back to its vfx block would mean re-running SyncLayers'
        /// validity filter on every tick.
        /// </summary>
        private readonly struct AmbientTarget
        {
            public readonly ParticleSystem System;
            public readonly ParticleVfxParams Params;
            public AmbientTarget(ParticleSystem system, ParticleVfxParams p) { System = system; Params = p; }
        }

        private readonly List<AmbientTarget> _ambientTargets = new List<AmbientTarget>();
        private Coroutine _ambientCoroutine;
        private WaitForSeconds _ambientWait;

        /// <summary>
        /// This emitter's own phase offset inside the tick window, held as a cached
        /// WaitForSeconds because the loop is STARTED far more often than once.
        /// ParticleInstancesLoader re-evaluates viewport culling every 0.2 s and SetActives
        /// whatever crossed the boundary; Unity kills every coroutine on an object it
        /// deactivates, so OnEnable — ResumeAmbientTracking — StartAmbientLoop runs again
        /// each time. An emitter parked ON that boundary therefore restarts up to five times
        /// a second, and a fresh `new WaitForSeconds(...)` per start was garbage at that rate
        /// across the ~150 vegetation emitters.
        ///
        /// Dropping the stagger instead would have been the wrong fix, and dropping it only
        /// on restarts doubly so: the culling pass re-enables a whole BATCH on one frame, so
        /// without an offset those emitters would resume in lockstep and recompute their tint
        /// on the same frame from then on — the exact pile-up the stagger exists to break.
        /// Randomised once and reused: one draw per emitter already spreads the set across
        /// the window, and a constant phase spreads a batch re-enable just as well as a
        /// fresh draw would.
        /// </summary>
        private WaitForSeconds _ambientStaggerWait;

        private Color _ambientApplied = Color.white;

        // ------------------------------------------------------------------ build-time hooks

        /// <summary>Clears the opt-in set at the start of every ApplyPreset, before any system
        /// is configured. A reused emitter (the F1 preview emitter serves every preset the
        /// user clicks) must not keep tracking the previous preset's systems.</summary>
        private void BeginAmbientPass() => _ambientTargets.Clear();

        /// <summary>Called by ConfigureParticleSystem for the root AND for every composite
        /// layer, so a stack whose light layer opts in tracks the cycle even when its mass
        /// layer does not. No-op for every preset that leaves the flag alone.</summary>
        private void RegisterAmbientTarget(ParticleSystem ps, ParticleVfxParams p)
        {
            if (ps == null || p == null || !p.respondsToAmbientLight) return;
            _ambientTargets.Add(new AmbientTarget(ps, p));
        }

        /// <summary>
        /// Starts or stops the tracking loop once the whole stack is configured.
        ///
        /// A coroutine rather than Update(): Unity pays the managed call for every
        /// MonoBehaviour that DEFINES an Update, opted in or not, and the vegetation pass
        /// places ~150 emitters — so an Update would charge every preset in the game for a
        /// value that moves twice a second. Nothing is started when nothing opted in, which
        /// is what makes the default flag literally free.
        ///
        /// Not event-driven either: DayNightCycle exposes OnPhaseChanged, which fires at
        /// phase BOUNDARIES only, while CurrentColor is a smoothstep RAMP across the whole
        /// Dawn and Dusk bands. Hooking the event would snap the tint once at the start of
        /// dawn and then hold it wrong for the following 432 seconds — exactly the window
        /// this feature exists for. There is no per-colour-change event to hook.
        /// </summary>
        private void EndAmbientPass()
        {
            if (_ambientTargets.Count == 0)
            {
                if (_ambientCoroutine != null) { StopCoroutine(_ambientCoroutine); _ambientCoroutine = null; }
                return;
            }
            // ConfigureParticleSystem already baked the current tint into every start colour
            // via BuildColorParameter, so record it as applied — otherwise the first tick
            // would rewrite all of them for no change.
            _ambientApplied = CurrentAmbientTint();
            StartAmbientLoop();
        }

        private void StartAmbientLoop()
        {
            if (_ambientCoroutine != null) return;
            // StartCoroutine logs and refuses on an inactive GameObject, and coroutines do not
            // run at all outside Play Mode (the Particles editor WINDOW applies presets to a
            // preview emitter in edit mode). OnEnable picks the culled case back up.
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (_ambientWait == null) _ambientWait = new WaitForSeconds(AmbientTickSeconds);
            if (_ambientStaggerWait == null)
                _ambientStaggerWait = new WaitForSeconds(UnityEngine.Random.value * AmbientTickSeconds);
            _ambientCoroutine = StartCoroutine(AmbientTintLoop());
        }

        /// <summary>
        /// Called from OnEnable. Unity halts every coroutine on a GameObject when it is
        /// deactivated and does NOT resume it on re-enable, while ParticleInstancesLoader
        /// deactivates off-screen emitters continuously — so the handle we hold is already
        /// dead and has to be dropped rather than trusted. The immediate re-apply matters
        /// because an emitter culled through a whole dawn would otherwise come back wearing
        /// night's tint and keep it until the next tick.
        /// </summary>
        private void ResumeAmbientTracking()
        {
            if (_ambientTargets.Count == 0) return;
            _ambientCoroutine = null;
            ApplyAmbientTint(force: true);
            StartAmbientLoop();
        }

        // ------------------------------------------------------------------ tracking

        private IEnumerator AmbientTintLoop()
        {
            // Stagger the phase up front, on EVERY start: a coroutine's cadence is fixed by
            // its first yield, so this initial wait is what spreads ~150 vegetation emitters
            // across the tick window instead of landing them all on the same frame — and a
            // cull-boundary batch coming back through OnEnable needs that spread just as much
            // as the first boot does. The wait object is hoisted rather than rebuilt, since
            // only the restart RATE made it expensive (see _ambientStaggerWait).
            yield return _ambientStaggerWait;
            while (true)
            {
                ApplyAmbientTint(force: false);
                yield return _ambientWait;
            }
        }

        /// <summary>
        /// Re-composes the start colour of every opted-in system against the live ambient.
        /// Note this only affects particles spawned from here on — Unity does not re-colour
        /// live ones. Over a 432 s ramp a 6 s leaf lifetime is under 1.5% of the swing, so the
        /// trailing generation is never distinguishable from the current one.
        /// </summary>
        private void ApplyAmbientTint(bool force)
        {
            Color amb = CurrentAmbientTint();
            if (!force
                && Mathf.Abs(amb.r - _ambientApplied.r) < AmbientEpsilon
                && Mathf.Abs(amb.g - _ambientApplied.g) < AmbientEpsilon
                && Mathf.Abs(amb.b - _ambientApplied.b) < AmbientEpsilon)
                return;

            _ambientApplied = amb;
            for (int i = 0; i < _ambientTargets.Count; i++)
            {
                var target = _ambientTargets[i];
                if (target.System == null) continue;
                // main.startColor is one of the properties Unity accepts on a PLAYING system
                // (unlike main.duration, which is not — see EnsureParticleSystem). Nothing
                // here stops or restarts anything, so emission is never interrupted.
                var main = target.System.main;
                main.startColor = BuildColorParameter(target.Params);
            }
        }

        /// <summary>
        /// The multiplier a preset that opted in is tinted by, or white for one that did not.
        /// Read by <see cref="BuildColorParameter"/> so the spawn-time build and the tracking
        /// loop cannot drift apart.
        /// </summary>
        private static Color AmbientTint(ParticleVfxParams p)
            => (p != null && p.respondsToAmbientLight) ? CurrentAmbientTint() : Color.white;

        /// <summary>
        /// The day/night cycle's live colour, floored per channel. White — an exact identity
        /// multiply — whenever no cycle is reachable: EditMode tests, the Particles editor
        /// window in edit mode, and the boot window before GameplaySceneSetup has created the
        /// cycle. DayNightCycle also forces CurrentColor to white while its LightingEnabled
        /// master switch is off, so "show me the world untinted" silences the particle tint
        /// too, for free.
        /// </summary>
        private static Color CurrentAmbientTint()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return Color.white;
            Color c = cycle.CurrentColor;
            return new Color(
                Mathf.Max(c.r, AmbientChannelFloor),
                Mathf.Max(c.g, AmbientChannelFloor),
                Mathf.Max(c.b, AmbientChannelFloor),
                1f);
        }
    }
}
