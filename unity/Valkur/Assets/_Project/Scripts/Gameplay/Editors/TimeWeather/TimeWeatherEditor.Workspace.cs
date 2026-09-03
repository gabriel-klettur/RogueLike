using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Time and Weather Editor (F2) — panel layout only.
    ///
    /// This editor deliberately captures NO state of its own, and that is the honest
    /// answer rather than a gap: everything it shows belongs to something else that
    /// already owns it. The hour and the phase belong to the live <c>DayNightCycle</c>,
    /// the per-zone levels to <c>WeatherManager</c>, and both are world state the player
    /// is standing in — not an authoring preference. Restoring "it was midnight and
    /// snowing" on a later session would overwrite the world the author actually loaded.
    ///
    /// Adopting the interface is still worth it: <see cref="WorkspaceRoot"/> is what lets
    /// the layer walk this editor panels, so their position, size and open/closed state
    /// are remembered like every other editor. That is the majority of what persistence
    /// buys here.
    /// </summary>
    public partial class TimeWeatherEditor : IProvidesWorkspaceState
    {
        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws) { }

        public void RestoreWorkspace(EditorWorkspace ws) { }
    }
}
