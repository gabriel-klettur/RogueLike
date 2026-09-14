using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The working session: the clock that turns "the player is standing here holding the key"
    /// into a rhythm of individual blows.
    ///
    /// <para>The rhythm is the design. An activity made only of CONTINUOUS motion stops being
    /// read after about a second, so a shift is a sequence of discrete blows — each a strike, a
    /// shudder, chips, a possible yield and a small camera beat — and <c>secondsPerBlow</c> is
    /// the one number that sets how the whole thing feels.</para>
    ///
    /// <para><b>THE SESSION PAYS NOTHING ITSELF.</b> A Destroy-mode blow goes through
    /// <see cref="BuildingDurability.ApplyObstacleDamage"/> exactly like a sword swing, and what
    /// it yields and teaches is decided where every blow arrives (<c>.Yield</c>). It used to roll
    /// a yield per blow on its own, regardless of the damage dealt — which made bare hands, at
    /// forty one-point blows, pay ten times what an axe paid for the same tree.</para>
    /// </summary>
    public partial class HarvestNode
    {
        private GameObject _worker;
        private bool _sessionActive;
        private float _nextBlowAt;

        public bool IsInteracting => _sessionActive;

        /// <summary>Blows landed in the current session. A test and diagnosis seam.</summary>
        public int SessionBlows { get; private set; }

        public void BeginInteraction(GameObject player)
        {
            if (_sessionActive || !CanInteract(player)) return;

            _worker = player;
            _sessionActive = true;
            SessionBlows = 0;
            ResetRhythm();
            RefreshTicking();

            EnsurePresentation();
            HarvestWorkMark.For(player)?.Engage(this);

            // The first blow lands immediately: waiting out an interval first reads as the press
            // having been missed, and the player presses again.
            LandBlow(RhythmVerdict.None);
        }

        public void CancelInteraction()
        {
            if (_worker != null) HarvestWorkMark.For(_worker)?.Release(this);
            _sessionActive = false;
            _worker = null;
            ResetRhythm();
            RefreshTicking();
        }

        private void TickSession()
        {
            if (!_sessionActive) return;

            if (!CanInteract(_worker)) { CancelInteraction(); return; }

            // Tapping owns the clock while it lasts; the automatic swing waits.
            if (_rhythmMode) { TickRhythm(); return; }
            if (Now < _nextBlowAt) return;

            NoteAutoBlow();
            LandBlow(RhythmVerdict.None);
        }

        /// <summary>
        /// One blow, from the automatic clock (<paramref name="verdict"/> None) or from a tap that
        /// landed on the beat. The tool is resolved PER BLOW, not latched when the session begins:
        /// equipping an axe halfway through a chop takes effect on the very next swing.
        /// </summary>
        private void LandBlow(RhythmVerdict verdict)
        {
            if (_profile == null) return;

            _lastBlowAt = Now;
            PlayWorkerSwing();

            // A CONSTANT clock. The resistance multiplier and the skill are spent on the blow's
            // WORTH and never on its timing: spending them on the clock as well would count them
            // twice, and on the clock instead is what once made mining and chopping two different
            // activities that looked like one.
            _nextBlowAt = Now + Mathf.Max(0.05f, _profile.secondsPerBlow);
            SessionBlows++;

            if (_profile.harvestMode == HarvestMode.Deplete)
            {
                var blow = HarvestBlowResolver.Resolve(_profile, _worker, element: null);
                NoteWorked(_worker);

                if (blow.Immune)
                {
                    RaiseBlowLanded(blow, 0);
                    return;
                }

                HarvestWorkMark.For(_worker)?.Strike(this);
                int yields = ApplyWork(_profile.blowDamage, blow, _worker);
                RaiseBlowLanded(blow, yields);
                return;
            }

            // Destroy mode: hand the blow to durability through the entry point a combat swing
            // uses. It raises Worked, and OnDurabilityWorked pays, teaches and reports — for this
            // blow and for every blow from any other source.
            ApplyDurabilityBlow();
        }

        private void ApplyDurabilityBlow()
        {
            if (_durability == null) return;

            Vector2 workerPosition = _worker != null
                ? (Vector2)_worker.transform.position
                : (Vector2)transform.position;

            Vector2 contact = WorkableBounds.ClosestPoint(workerPosition);
            _durability.ApplyObstacleDamage(_profile.blowDamage, _worker, contact, element: null);
        }

        /// <summary>
        /// Play the worker's swing through <see cref="PlayerController.PlayWorkSwing"/> — locomotion
        /// reverts any animator state it does not recognise on the very next frame, so writing the
        /// animator directly from here would never render.
        /// </summary>
        private void PlayWorkerSwing()
        {
            if (_worker == null) return;

            var controller = _worker.GetComponent<PlayerController>();
            if (controller == null) return;

            Vector2 toNode = WorkableBounds.center - _worker.transform.position;

            // Which animation a swing plays is a property of the THING BEING WORKED: a tree names
            // "harvest_chop" and a seam "harvest_mine". A character with no art for the key falls
            // back to the ordinary attack rotation. The blow interval goes with the key so the
            // animation can be paced to last exactly one blow.
            string key = _profile != null ? _profile.swingAnimationKey : null;
            // While tapping the swing is paced to the BEAT, or a faster rhythm would restart the
            // animation before its deepest frames render — the cut this pacing exists to prevent.
            float blowSeconds = _profile == null ? 0f : (_rhythmMode ? BeatSeconds : _profile.secondsPerBlow);
            controller.PlayWorkSwing(toNode, key, blowSeconds);
        }
    }
}
