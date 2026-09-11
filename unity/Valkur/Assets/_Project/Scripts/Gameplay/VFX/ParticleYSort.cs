using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Orders a placed emitter against the world by its own Y, using the SAME formula every
    /// building and entity is ordered by (<see cref="SortingConfig.YToSortingOrder"/>).
    ///
    /// <para>WHY THIS EXISTS. A sorting LAYER buys the band and nothing finer. Buildings sit on
    /// <c>PropsL{z}</c> and take their order from their Y, so inside that band a fixed
    /// <c>sortingOrder</c> is either in front of every building or behind every one of them —
    /// putting a torch flame behind ONE house and in front of its neighbour meant the author
    /// working out <c>-(y * 100)</c> by hand and typing it into a box.</para>
    ///
    /// <para>THE AUTHORED ORDER IS KEPT AS A RELATIVE NUDGE, never discarded. Each renderer's
    /// order at bind time is its base, and the Y term is ADDED to it. That is what keeps a
    /// composite preset's layers ordered against each other — they are separate systems with
    /// separate authored orders and one shared Y — and it lets an author bias one emitter a
    /// little in front of the thing it belongs to. Replacing the order instead would collapse
    /// every layer of a composite onto one value and leave <c>sortingFudge</c> as the only
    /// tie-break, which is the state <c>VortexFunnelFX</c> records as unreadable.</para>
    ///
    /// <para>IT IS ATTACHED BY <see cref="ParticleEmitter"/>, never by hand, and only for an
    /// emitter carrying a <c>PersistedParticleInstance</c> — see <c>RefreshYSort</c> there for
    /// why a preview emitter and a spell's effect are excluded.</para>
    ///
    /// <para>Renderer, not SpriteRenderer: a lightning preset draws through a LineRenderer, and
    /// an emitter that opted into Y-sort must not silently be exempt because of which component
    /// happens to draw it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParticleYSort : MonoBehaviour
    {
        private Renderer[] _renderers;
        private int[]      _baseOrders;

        /// <summary>
        /// Y of the last write. NaN forces the next one, which is what makes
        /// <see cref="Rebind"/> land immediately instead of a frame later.
        /// </summary>
        private float _lastY = float.NaN;

        /// <summary>
        /// Snapshot the renderers and the orders the apply just wrote, then order once.
        ///
        /// <para>MUST run AFTER the emitter has configured its systems: <c>ConfigureRenderer</c>
        /// writes each block's authored <c>sortingOrder</c>, and that value IS the base this
        /// captures. Binding first would capture whatever the previous preset left behind and
        /// bake it in for the life of the placement.</para>
        /// </summary>
        public void Rebind()
        {
            _renderers  = GetComponentsInChildren<Renderer>(true);
            _baseOrders = new int[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _baseOrders[i] = _renderers[i] != null ? _renderers[i].sortingOrder : 0;

            _lastY = float.NaN;
            ApplyNow();
        }

        /// <summary>
        /// Write the order for the current Y. Public because Edit Mode never calls
        /// <c>LateUpdate</c>, so a fixture — and <see cref="Rebind"/> itself — has to be able
        /// to ask for the pass rather than wait for a frame that never comes.
        /// </summary>
        public void ApplyNow()
        {
            if (_renderers == null) return;

            float y = transform.position.y;
            // Not Mathf.Approximately: it scales its epsilon with the magnitude, and at the far
            // end of the world that tolerance is wider than the hundredth of a unit the Y-sort
            // exists to resolve. An exact compare is also what makes the common case — an
            // emitter that has not moved — a single float test.
            if (y == _lastY) return;
            _lastY = y;

            int yTerm = SortingConfig.YToSortingOrder(y);
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.sortingOrder = _baseOrders[i] + yTerm;
            }
        }

        // LateUpdate rather than Update, for the reason YSortEntity already carries: whatever
        // moved the emitter this frame — an author's drag, the Selection tool, a parent — has
        // finished moving it by now.
        private void LateUpdate() => ApplyNow();
    }
}
