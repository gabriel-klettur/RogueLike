using System;
using System.IO;
using UnityEngine;
using Valkur.Gameplay.Save;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The explored map survives the session: it is written beside the run's saves and read
    /// back when the run is loaded.
    ///
    /// <para><b>Per RUN, not per save slot.</b> Exploration is knowledge, and knowledge only
    /// grows: loading an older save of the same run does not make the player forget a valley
    /// they have walked. So there is one file per run, and reading it MERGES (the larger value
    /// per cell wins) instead of replacing.</para>
    ///
    /// <para><b>Never a phantom run.</b> The file is written only into a run folder that
    /// already exists — i.e. one the save system created — so a session that never saved does
    /// not leave a folder the pruner has to reason about. Its extension is not
    /// <c>.json</c>, which is what the save listing enumerates, so it is never mistaken for a
    /// save. And it is written only in Play Mode: an EditMode fixture that builds a minimap
    /// must not be able to reach <c>persistentDataPath</c> (the twin-save incident).</para>
    /// </summary>
    public partial class MinimapManager
    {
        private const string FogFileName = "minimap_fog.minimap";
        private const float FogSaveInterval = 15f;

        private string _fogRunId;
        private bool _fogRunResolved;
        private int _savedFogRevision = -1;
        private float _nextFogSave;
        private bool _warnedFogWrite;

        /// <summary>
        /// Follow the active run: load its fog when it changes, write the live fog every few
        /// seconds when it has changed. Called by the HUD every frame.
        /// </summary>
        public void TickPersistence(float now)
        {
            if (!Application.isPlaying) return;
            string run = SaveFileManager.ActiveRunId;
            if (!_fogRunResolved || run != _fogRunId)
            {
                bool switchingRuns = _fogRunResolved && !string.IsNullOrEmpty(_fogRunId) && run != _fogRunId;
                if (switchingRuns)
                {
                    SaveFogNow();
                    Fog.ClearAll();
                }
                // From "no run yet" to a run keeps what was explored meanwhile and merges the
                // file on top: the save service names the run a moment after the world loads,
                // and the player may already have taken a few steps.
                _fogRunId = run;
                _fogRunResolved = true;
                LoadFogFor(run);
            }

            if (now >= _nextFogSave)
            {
                _nextFogSave = now + FogSaveInterval;
                if (_fog != null && _fog.Revision != _savedFogRevision) SaveFogNow();
            }
        }

        private static string FogPathFor(string run)
            => string.IsNullOrEmpty(run) ? null : Path.Combine(SaveFileManager.GetRunDirectory(run), FogFileName);

        private void LoadFogFor(string run)
        {
            string path = FogPathFor(run);
            if (path == null || !File.Exists(path)) return;
            try
            {
                if (Fog.MergeJson(File.ReadAllText(path))) _savedFogRevision = Fog.Revision;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Minimap] Could not read the explored map at {path}: {e.Message}");
            }
        }

        /// <summary>Write the explored map now, if anything changed and the run has a folder.</summary>
        public void SaveFogNow()
        {
            if (!Application.isPlaying || _fog == null || string.IsNullOrEmpty(_fogRunId)) return;
            if (_fog.Revision == _savedFogRevision) return;
            string dir = SaveFileManager.GetRunDirectory(_fogRunId);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            string path = Path.Combine(dir, FogFileName);
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, _fog.ToJson());
                File.Copy(tmp, path, true);
                _savedFogRevision = _fog.Revision;
            }
            catch (Exception e)
            {
                if (!_warnedFogWrite)
                {
                    _warnedFogWrite = true;
                    Debug.LogWarning($"[Minimap] Could not write the explored map to {path}: {e.Message}");
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { /* best effort */ }
            }
        }
    }
}
