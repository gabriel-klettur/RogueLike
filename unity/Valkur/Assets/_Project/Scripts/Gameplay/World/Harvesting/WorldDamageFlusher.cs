using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Writes <see cref="WorldDamageService"/> to disk on a slow timer and on the way out.
    ///
    /// <para>WHY A TIMER RATHER THAN A WRITE PER BLOW. Chopping a tree lands a blow roughly
    /// twice a second and every one of them changes the record. Writing each would rewrite the
    /// whole file dozens of times per tree, through an atomic tmp+replace, for a value that is
    /// only ever read at load. Coalescing costs at most <see cref="FLUSH_INTERVAL_SECONDS"/> of
    /// progress on a hard crash, and the run's rotating save backups already cover the case
    /// where that matters.</para>
    ///
    /// <para>The quit and disable hooks are the half that makes the timer safe: a player who
    /// fells a tree and immediately quits would otherwise lose it. <c>OnApplicationQuit</c>
    /// does not run when the Editor leaves Play Mode, which is why <c>OnDisable</c> is here
    /// too — and a double flush is free, because a clean table writes nothing.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldDamageFlusher : MonoBehaviour
    {
        private const float FLUSH_INTERVAL_SECONDS = 5f;

        private WorldDamageService _service;
        private float _nextFlushAt;

        public void Bind(WorldDamageService service)
        {
            _service = service;
            _nextFlushAt = Time.unscaledTime + FLUSH_INTERVAL_SECONDS;
        }

        private void Update()
        {
            if (_service == null || !_service.IsDirty) return;
            if (Time.unscaledTime < _nextFlushAt) return;

            _nextFlushAt = Time.unscaledTime + FLUSH_INTERVAL_SECONDS;
            _service.Flush();
        }

        private void OnApplicationQuit() => _service?.Flush();

        private void OnDisable()
        {
            // Leaving Play Mode in the Editor never raises OnApplicationQuit, so without this
            // an Editor session's last few seconds of chopping would be lost every time — and
            // the loss would look like the persistence layer being broken rather than late.
            _service?.Flush();
        }
    }
}
