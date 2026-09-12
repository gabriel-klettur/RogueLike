using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The board's event particles, and the respec, which is the only reversible one.
    ///
    /// <para><b>Five events and only five</b> (R8): a rank bought, a path opening, the capstone,
    /// a point arriving, and a respec draining the board. Nothing emits at rest, nothing emits on
    /// hover, and nothing emits on a REFUSAL — a refused purchase shakes its cost instead
    /// (<c>RefuseAt</c>). A board where three of seven nodes glowed because they were affordable
    /// would be animated permanently, and R8's whole argument is that a HUD which shines without
    /// cause stops warning when there is a cause.</para>
    ///
    /// <para>Positions are in the pixel root's texel space, which is what the mote layer draws
    /// in, so a burst from a node has to add the board's own offset to the node's socket centre.</para>
    /// </summary>
    public sealed partial class SkillTreeHUD
    {
        private bool _respecArmed;
        private float _respecArmedUntil;
        private int _respecDrainIndex = -1;
        private float _respecNextDrain;

        /// <summary>True while the respec button is waiting for its second click.</summary>
        public bool RespecArmed => _respecArmed;

        /// <summary>
        /// Presses the respec button. Unity raises no pointer event in Edit Mode, so a fixture
        /// that wants to prove the two-click guard has no other way in — and that guard is the
        /// only thing between a stray click and a lost build.
        /// </summary>
        public void ClickRespecForTests() => OnRespecClicked();

        // ── Emitters ──────────────────────────────────────────────────────────

        /// <summary>
        /// The rank itself: motes thrown out of the socket in the colour of the stat that moved,
        /// so the burst says WHICH number went up without the player reading the card.
        /// </summary>
        private void EmitRankBurst(SkillNodeView view)
        {
            if (_motes == null || view == null) return;
            Vector2 origin = BoardToPixels(view.SocketCentre);
            Color c = StatColour(view.Node);

            int n = _style.motesPerRank;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n + Random.value * 0.3f;
                var vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(18f, 34f);
                _motes.Emit(origin, vel, c, _style.moteLifeSeconds, HudMoteShape.Dot,
                            gravity: 14f, drag: 1.8f, twinkle: true);
            }
        }

        /// <summary>
        /// The consequence the player is not looking at: the node they just finished is up on the
        /// board and what it opens is below it, so the ring marks the arrival rather than the
        /// purchase.
        /// </summary>
        private void EmitUnlockRings(SkillNode finished)
        {
            if (_motes == null || _boundTree == null) return;

            for (int i = 0; i < _views.Count; i++)
            {
                var view = _views[i];
                if (view.Node == null || view.Node.prerequisites == null) continue;

                bool opens = false;
                foreach (var prereq in view.Node.prerequisites)
                    if (prereq == finished) { opens = true; break; }
                if (!opens) continue;

                Vector2 origin = BoardToPixels(view.SocketCentre);
                int n = _style.motesPerUnlock;
                for (int k = 0; k < n; k++)
                {
                    float a = Mathf.PI * 2f * k / n;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    _motes.Emit(origin + dir * 16f, dir * 10f, _theme.gold,
                                _style.moteLifeSeconds, HudMoteShape.Star, drag: 3f);
                }
            }
        }

        /// <summary>A tree is finished once per run. The only event allowed to touch the chrome.</summary>
        private void EmitCapstoneFlash(SkillNodeView view)
        {
            if (_motes == null) return;
            Vector2 origin = BoardToPixels(view.SocketCentre);
            for (int i = 0; i < _style.motesPerRank * 2; i++)
            {
                float a = Mathf.PI * 2f * i / (_style.motesPerRank * 2f);
                var vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(26f, 52f);
                _motes.Emit(origin, vel, _theme.gold, _style.moteLifeSeconds, HudMoteShape.Glow,
                            drag: 1.4f, twinkle: true);
            }
        }

        /// <summary>
        /// A point arriving — from a level, from a quest. It FALLS onto the medallion, so the
        /// number the player has to spend is where their eye ends up.
        /// </summary>
        private void EmitPointArrived()
        {
            if (_motes == null || _pointsMedallion == null) return;
            var rt = _pointsMedallion.rectTransform;
            var centre = rt.anchoredPosition + rt.sizeDelta * 0.5f;

            for (int i = 0; i < _style.motesPerPoint; i++)
            {
                var from = centre + new Vector2(Random.Range(-8f, 8f), 22f + i * 5f);
                _motes.Emit(from, new Vector2(0f, -26f), _theme.gold, _style.moteLifeSeconds,
                            HudMoteShape.Plus, gravity: -40f, drag: 0.6f);
            }
        }

        /// <summary>Board texels to the pixel root's space — the mote layer fills the latter.</summary>
        private Vector2 BoardToPixels(Vector2 boardPoint)
        {
            return boardPoint + _boardRoot.anchoredPosition;
        }

        /// <summary>
        /// The colour a burst comes out in. One implementation, on the node view, so the sigil
        /// drawn on a socket and the motes thrown out of it can never disagree about what the
        /// talent does.
        /// </summary>
        private Color StatColour(SkillNode node) => SkillNodeView.StatColour(node, _theme);

        // ── Points arriving ───────────────────────────────────────────────────

        private int _lastSeenPoints = -1;

        /// <summary>
        /// Notices a point that arrived from outside this window. The model now raises
        /// <c>OnLoadoutChanged</c> from <c>AddPoints</c>, so the repaint is already happening;
        /// this only decides whether the balance went UP, which is the half that is an event.
        /// </summary>
        private void NotePointBalance()
        {
            if (skills == null) return;
            int now = skills.AvailablePoints;
            if (_lastSeenPoints >= 0 && now > _lastSeenPoints) EmitPointArrived();
            _lastSeenPoints = now;
        }

        // ── Respec ────────────────────────────────────────────────────────────

        /// <summary>
        /// Two clicks, and the armed state expires. Coming back to a red "¿Seguro?" with no
        /// memory of having pressed anything is one click away from losing a whole build — the
        /// same reasoning the quest log's abandon button already carries.
        /// </summary>
        private void OnRespecClicked()
        {
            if (skills == null) return;

            if (!_respecArmed)
            {
                if (skills.SpentPoints <= 0) return;   // nothing to undo; do not arm a no-op
                _respecArmed = true;
                _respecArmedUntil = Time.unscaledTime + 3f;
                _respecLabel.SetText(SkillText.RespecConfirm.ToUpperInvariant());
                _respecLabel.color = _theme.danger;
                _respecButton.color = _theme.stoneLight;
                return;
            }

            StartRespecDrain();
            skills.Respec();

            // The refund raises the balance, which is normally the "a point arrived" event. Here
            // it is the drain's own doing, so the balance is re-baselined rather than announced:
            // two answers to one act is how a HUD stops meaning anything.
            _lastSeenPoints = skills.AvailablePoints;

            DisarmRespec();
            Repaint();
        }

        private void DisarmRespec()
        {
            if (!_respecArmed) return;
            _respecArmed = false;
            if (_respecLabel != null)
            {
                _respecLabel.SetText(SkillText.Respec.ToUpperInvariant());
                _respecLabel.color = _theme.textDim;
            }
            if (_respecButton != null) _respecButton.color = _theme.stoneDark;
        }

        private void TickRespecArm(float now)
        {
            if (_respecArmed && now > _respecArmedUntil) DisarmRespec();
            TickRespecDrain(now);
            TickRefuse(now);
            NotePointBalance();
        }

        /// <summary>
        /// The one event that runs backwards: motes leave each bought node and drain toward the
        /// medallion, staggered, so a respec reads as the points coming back rather than as the
        /// board simply emptying.
        /// </summary>
        private void StartRespecDrain()
        {
            _respecDrainIndex = 0;
            _respecNextDrain = Time.unscaledTime;
        }

        private void TickRespecDrain(float now)
        {
            if (_respecDrainIndex < 0 || _motes == null) return;
            if (now < _respecNextDrain) return;

            while (_respecDrainIndex < _views.Count)
            {
                var view = _views[_respecDrainIndex++];
                if (view.State == SkillNodeState.Maxed || view.State == SkillNodeState.Partial ||
                    _respecDrainIndex == _views.Count)
                {
                    Vector2 from = BoardToPixels(view.SocketCentre);
                    var rt = _pointsMedallion.rectTransform;
                    Vector2 to = rt.anchoredPosition + rt.sizeDelta * 0.5f;
                    Vector2 dir = (to - from).normalized;
                    _motes.Emit(from, dir * 90f, _theme.gold, _style.moteLifeSeconds,
                                HudMoteShape.Dot, drag: 0.4f);
                    _respecNextDrain = now + _style.respecStaggerSeconds;
                    return;
                }
            }

            _respecDrainIndex = -1;
        }
    }
}
