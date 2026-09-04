using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The working session: the clock that turns "the player is standing here holding the
    /// node" into a rhythm of individual blows.
    ///
    /// <para>The rhythm is the design. An activity made only of CONTINUOUS motion — dust
    /// rising at a steady rate, a bar creeping up — stops being read after about a second;
    /// the same measurement that made the vortex's discharges an EVENT rather than a glow.
    /// So a shift is a sequence of discrete blows, each of which is a strike, a sound, a
    /// possible yield and a small camera beat, and <c>secondsPerBlow</c> is the one number
    /// that sets how the whole thing feels.</para>
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
            RefreshTicking();

            // Built on first use, not at spawn, and through the same helper the swung path
            // uses so the two ways in cannot end up with different rigs.
            EnsurePresentation();

            // The first blow lands immediately. Pressing the key has to produce a visible
            // consequence on the same frame; waiting out an interval first reads as the press
            // having been missed, and the player presses again.
            LandBlow();
        }

        public void CancelInteraction()
        {
            _sessionActive = false;
            _worker = null;
            RefreshTicking();
        }

        private void TickSession()
        {
            if (!_sessionActive) return;

            if (!CanInteract(_worker)) { CancelInteraction(); return; }
            if (Time.time < _nextBlowAt) return;

            LandBlow();
        }

        /// <summary>
        /// One blow: judge it, apply it in whichever way this mode applies blows, roll the
        /// per-blow yield, and schedule the next.
        ///
        /// <para>The tool is resolved PER BLOW, not latched when the session begins. Equipping
        /// an axe halfway through a chop takes effect on the very next swing — measured live
        /// on a shipped tree: nineteen blows that started bare-handed at 1 damage and finished
        /// at 10 after the axe went on. That is the behaviour to keep, and it is written down
        /// because the opposite is the natural assumption: the alternative is a player who
        /// equips the right tool, sees nothing change, and concludes the tool does not work.
        /// It also costs nothing — the resolve is a table lookup over the equipment slots.</para>
        /// </summary>
        private void LandBlow()
        {
            if (_profile == null) return;

            var blow = HarvestBlowResolver.Resolve(_profile, _worker, element: null);

            PlayWorkerSwing();

            // A CONSTANT clock, in both modes. The resistance multiplier is spent on the
            // blow's WORTH and never on its timing: spending it on the clock as well would
            // count it twice, and spending it on the clock instead is what made mining and
            // chopping two different activities that looked like one.
            _nextBlowAt = Time.time + Mathf.Max(0.05f, _profile.secondsPerBlow);

            // A multiplier of exactly zero is a deliberate immunity. The blow still happens —
            // the swing, the sound, the bounce — but nothing is consumed and nothing drops,
            // which is what tells the player their tool is the problem rather than the node.
            if (blow.Immune)
            {
                BlowLanded?.Invoke(blow, 0);
                return;
            }

            // Yields land at the worker's feet in BOTH modes -- see WorkerDropPoint.
            Vector3 drop = WorkerDropPoint(_worker);

            int yields;
            if (_profile.harvestMode == HarvestMode.Deplete)
                yields = ApplyWork(_profile.blowDamage, blow, drop);
            else
            {
                yields = RollYield(drop);
                ApplyDurabilityBlow(blow);
            }

            SessionBlows++;
            BlowLanded?.Invoke(blow, yields);
        }

        /// <summary>
        /// Hand the blow to <see cref="BuildingDurability"/> through the same entry point a
        /// combat swing uses, rather than reaching into its durability field. That keeps the
        /// death sequence, the drop table, the remains and the collision clear on ONE path:
        /// a tree felled by an axe and a tree chopped down by hand must not end differently.
        /// </summary>
        private void ApplyDurabilityBlow(HarvestBlow blow)
        {
            if (_durability == null) return;

            Vector2 workerPosition = _worker != null
                ? (Vector2)_worker.transform.position
                : (Vector2)transform.position;

            Vector2 contact = WorkableBounds.ClosestPoint(workerPosition);
            _durability.ApplyObstacleDamage(_profile.blowDamage, _worker, contact, element: null);
        }

        /// <summary>
        /// Roll the per-blow table. This is what a mine actually produces; the profile's
        /// <c>drops</c> stays what a Destroy-mode building leaves when it finally falls, so a
        /// tree can pay out once at the end and a seam can pay out all the way through.
        /// </summary>
        private int RollYield(Vector3 origin)
        {
            // The weighted pool wins when both are authored: it is the one that makes the
            // lines COMPETE, which is what a seam wants. A profile carrying both is not an
            // error -- a designer may keep an independent table around while trying a pool.
            if (_profile.yieldPool != null)
                return HarvestDropResolver.SpawnFromPool(_profile.yieldPool, origin);

            if (_profile.yieldPerBlow == null) return 0;
            return HarvestDropResolver.SpawnDrops(_profile.yieldPerBlow, origin);
        }

        /// <summary>
        /// Play the worker's swing.
        ///
        /// <para>It goes through <see cref="PlayerController.PlayWorkSwing"/> rather than
        /// writing the animator directly, because locomotion reverts any state it does not
        /// recognise on the very next frame: a pose set from here would be overwritten before
        /// it rendered, which is invisible in code and reads on screen as the character
        /// standing still while the rock loses charges.</para>
        /// </summary>
        private void PlayWorkerSwing()
        {
            if (_worker == null) return;

            var controller = _worker.GetComponent<PlayerController>();
            if (controller == null) return;

            Vector2 toNode = WorkableBounds.center - _worker.transform.position;

            // Which animation a swing plays is a property of the THING BEING WORKED, not of
            // the code that swings: a tree names "harvest_chop" and a seam "harvest_mine", and
            // a third kind of node needs no code here at all. A character with no art for the
            // key falls back to the ordinary attack rotation, which is why only the dwarf
            // needs sheets for this to ship.
            // The blow interval goes with the key, so the animation can be paced to last
            // exactly one blow. Without it a swing longer than the interval is restarted
            // before it finishes and the deepest frames never render.
            string key = _profile != null ? _profile.swingAnimationKey : null;
            float blowSeconds = _profile != null ? _profile.secondsPerBlow : 0f;
            controller.PlayWorkSwing(toNode, key, blowSeconds);
        }
    }
}
