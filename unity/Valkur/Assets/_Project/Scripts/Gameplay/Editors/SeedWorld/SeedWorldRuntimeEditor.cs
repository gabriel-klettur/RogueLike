using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data.WorldGen;
using Debug = UnityEngine.Debug;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// Runtime Seed World editor, opened from the General Editor (ESC -> Seed World).
    ///
    /// <para><b>WHAT IT IS FOR.</b> Configure a procedural world — seed, size, continents,
    /// climate, which biomes may appear and how much each claims — and see the result at once.
    /// It previews the world (phase 1 of <c>.github/SEED_WORLD_ROADMAP.md</c>) and builds it
    /// into its own map slot with rivers and towns (phases 2 and 3, <c>.Build.cs</c>).</para>
    ///
    /// <para><b>The preview is the generator.</b> Every pixel is one call to
    /// <see cref="WorldClimate.Sample"/> + <see cref="WorldClimate.Classify"/> through
    /// <see cref="WorldGenMap"/>, the same functions the build will call. Nothing in this folder
    /// decides what a biome is.</para>
    ///
    /// <para><b>It edits a VALUE, not an asset.</b> The settings live in this editor (and its
    /// workspace document). Only "Construir" writes anything, and only into a map slot of its
    /// own — never the base world.</para>
    ///
    /// <para><b>NO HOTKEY</b>: it opens from the launcher. Its only tools are the floating camera's
    /// (<c>Editor.SeedWorld</c>: fly; Shift held flies fast), live only while "Visualizar mapa" is up
    /// (<c>.Viewer.cs</c>); every other verb is a button, and undo/redo come from <c>EditorShared</c>.</para>
    /// </summary>
    public sealed partial class SeedWorldRuntimeEditor : SingletonMonoBehaviour<SeedWorldRuntimeEditor>,
        GameEditorManager.IGameEditor
    {
        internal enum Tab
        {
            World = 0,
            Climate = 1,
            Biomes = 2,
        }

        internal enum PreviewLayer
        {
            Biome = 0,
            Elevation = 1,
            Temperature = 2,
            Humidity = 3,
            Rarity = 4,
        }

        private const int HISTORY_DEPTH = 64;

        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;

        private Tab _tab = Tab.World;
        private PreviewLayer _layer = PreviewLayer.Biome;

        private WorldGenSettings _settings = new WorldGenSettings();
        private WorldGenMap _map;
        private long _lastGenerateMs;

        /// <summary>JSON snapshots of the settings BEFORE each edit, newest last.</summary>
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();

        private readonly System.Random _seedRng = new System.Random();

        /// <summary>
        /// The EXACT string the General Editor opens this editor by, and the string inside the
        /// input context id. A mismatch there once silently killed all 35 tools of another editor.
        /// </summary>
        public string EditorName => "Seed World";

        public bool IsActive => _active;

        internal Tab ActiveTab => _tab;
        internal PreviewLayer ActiveLayer => _layer;
        internal WorldGenSettings Settings => _settings;
        internal WorldGenMap Map => _map;
        internal int UndoDepth => _undo.Count;
        internal int RedoDepth => _redo.Count;

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            // A session ending mid-view loads nothing: the return ticket brings the next one home.
            FinishViewing();
            ReleasePreviewTexture();
            base.OnDestroy();
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                // A BuildUI that throws half way leaves an editor registered, inactive and
                // impossible to open again; the Quests, Death and Camera editors carry the same guard.
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[SeedWorldEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            if (_root != null) _root.SetActive(true);
            ForcePanelsOpen();
            RebuildBody();
            Regenerate();
        }

        public void Deactivate()
        {
            CloseViewForDeactivate();
            _active = false;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        private void Update()
        {
            if (!_active) return;
            if (TickViewer()) return;
            if (EditorInput.UndoPressed()) Undo();
            else if (EditorInput.RedoPressed()) Redo();
        }

        // ── Generation ─────────────────────────────────────────────────────────

        /// <summary>Re-samples the preview from the current settings and repaints everything.</summary>
        internal void Regenerate()
        {
            _settings.Clamp();

            var sw = Stopwatch.StartNew();
            _map = WorldGenMap.Generate(_settings);
            sw.Stop();
            _lastGenerateMs = sw.ElapsedMilliseconds;

            if (_uiBuilt) RefreshPreview();
        }

        // ── Edits and history ──────────────────────────────────────────────────

        /// <summary>
        /// Apply one edit through the history. The snapshot is the whole settings value as JSON,
        /// so an undo restores exactly what was there — including the clamp a field applied — and
        /// no edit can exist without its inverse.
        /// </summary>
        internal void Commit(string label, Action<WorldGenSettings> edit)
        {
            if (edit == null) return;

            string before = _settings.ToJson();
            edit(_settings);
            _settings.Clamp();
            string after = _settings.ToJson();

            if (before == after) { ResyncFields(); return; }

            _undo.Add(before);
            if (_undo.Count > HISTORY_DEPTH) _undo.RemoveAt(0);
            _redo.Clear();

            SetStatus(label);
            AfterSettingsChanged();
        }

        internal void Undo()
        {
            if (_undo.Count == 0) { SetStatus("Nada que deshacer."); return; }
            _redo.Add(_settings.ToJson());
            RestoreSnapshot(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            SetStatus("Deshecho.");
            AfterSettingsChanged();
            RebuildBody(); // an undone toggle must redraw its SI/NO; no field has focus on an undo
        }

        internal void Redo()
        {
            if (_redo.Count == 0) { SetStatus("Nada que rehacer."); return; }
            _undo.Add(_settings.ToJson());
            RestoreSnapshot(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            SetStatus("Rehecho.");
            AfterSettingsChanged();
            RebuildBody();
        }

        private void RestoreSnapshot(string json)
        {
            var restored = WorldGenSettings.FromJson(json);
            if (restored != null) _settings = restored;
        }

        private void AfterSettingsChanged()
        {
            // A biome toggle changes how its row is DRAWN, so that tab rebuilds; every numeric
            // field re-syncs in place instead, or the box the author just tabbed into vanishes.
            if (_tab == Tab.Biomes) RebuildBody();
            else ResyncFields();
            Regenerate();
        }

        // ── Actions ────────────────────────────────────────────────────────────

        internal void SetTab(Tab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            RebuildBody();
        }

        internal void SetLayer(PreviewLayer layer)
        {
            if (_layer == layer) return;
            _layer = layer;
            if (_uiBuilt) RefreshPreview();
        }

        internal void RandomSeed()
        {
            int seed = WorldSeed.NewRandom(_seedRng);
            Commit($"Semilla aleatoria: {seed}", s => s.seed = seed);
        }

        /// <summary>A number is used as-is; any other text is hashed (see <see cref="WorldSeed"/>).</summary>
        internal void SetSeedText(string text)
        {
            if (!WorldSeed.TryParse(text, out int seed)) { ResyncFields(); return; }
            Commit($"Semilla: {seed}", s => s.seed = seed);
        }

        internal void ResetToDefaults()
        {
            int keepSeed = _settings.seed;
            Commit("Valores por defecto (la semilla se conserva).", s =>
            {
                var fresh = new WorldGenSettings { seed = keepSeed };
                JsonUtility.FromJsonOverwrite(fresh.ToJson(), s);
            });
        }

        internal void SetBiomeEnabled(WorldBiome biome, bool enabled)
        {
            string name = WorldBiomeTable.Get(biome).DisplayName;
            Commit($"{name}: {(enabled ? "activado" : "desactivado")}", s => s.WeightOf(biome).enabled = enabled);
        }
    }
}
