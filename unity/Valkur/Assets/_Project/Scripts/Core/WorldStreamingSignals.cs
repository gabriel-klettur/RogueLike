using System;

namespace Valkur.Core
{
    /// <summary>
    /// What a system that streams the world in and out tells the systems that WATCH tiles change.
    ///
    /// <para><b>Unloading is not erasing.</b> When the live Seed World drops a far zone it clears
    /// that zone's tiles, and <c>Tilemap.tilemapTileChanged</c> reports the clear exactly as it
    /// reports an author erasing them. The minimap bakes the world's own art into a terrain atlas
    /// and rebakes whatever changes, so without this it would repaint every explored zone the
    /// player walked away from as empty ground — the map would forget the world behind them.
    /// The minimap is <c>Valkur.UI</c> and the streamer <c>Valkur.Gameplay</c>, which may not
    /// reference each other, so the fact travels through Core.</para>
    ///
    /// <para>The callback is synchronous (measured: <c>SetTile</c> and <c>SetTilesBlock</c> both
    /// raise it before returning), so a scope around the clear is enough.</para>
    /// </summary>
    public static class WorldStreamingSignals
    {
        private static int s_unloadDepth;

        // Domain Reload is OFF: a scope left open by an exception in one Play session would
        // otherwise blind the minimap for every session after it.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            s_unloadDepth = 0;
        }

        /// <summary>True while tiles are being removed because their zone streamed OUT.</summary>
        public static bool IsUnloadingTiles => s_unloadDepth > 0;

        /// <summary>Open around a streaming unload; dispose to close. Nests.</summary>
        public static IDisposable UnloadingTiles()
        {
            s_unloadDepth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _closed;

            public void Dispose()
            {
                if (_closed) return;
                _closed = true;
                if (s_unloadDepth > 0) s_unloadDepth--;
            }
        }
    }
}
