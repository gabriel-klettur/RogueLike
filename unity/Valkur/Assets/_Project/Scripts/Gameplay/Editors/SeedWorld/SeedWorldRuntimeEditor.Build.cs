using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// "Construir": bake the previewed world into a map slot and load it. Phase 2 of
    /// <c>.github/SEED_WORLD_ROADMAP.md</c>.
    ///
    /// <para><b>The slot is the unit of safety.</b> The bake writes a NAMED map slot — never the
    /// base world — through <see cref="SeedWorldBaker"/>, which refuses a slot that exists and
    /// was not made by Seed World. Overwriting a Seed World slot takes a second click, and the
    /// way back is one button: load <c>default</c>.</para>
    ///
    /// <para><b>Rebuilding the slot that is loaded right now leaves it first.</b>
    /// <c>LoadMapSlot</c> begins by mirroring the live zone list INTO the outgoing slot's file;
    /// baking the active slot and then loading it would copy the old zones straight over the file
    /// the bake just wrote.</para>
    /// </summary>
    public partial class SeedWorldRuntimeEditor
    {
        private string _bakeSlotName = string.Empty;
        private string _armedOverwriteSlot;
        private TextMeshProUGUI _bakeReport;

        /// <summary>The slot a bake targets: the typed name, or <c>seed_&lt;seed&gt;</c> when empty.</summary>
        internal string BakeSlotName
            => string.IsNullOrWhiteSpace(_bakeSlotName) ? $"seed_{_settings.seed}" : _bakeSlotName.Trim();

        private void BuildBakeSection(Transform body)
        {
            EditorUIHelpers.BuildSectionHeader(body, "Construir");

            var nameRow = MakeRow(body, "BakeNameRow", ROW_H + 4f);
            AddCaption(nameRow.transform, "Mapa");
            var field = EditorUIHelpers.AddInputField(nameRow.transform, _bakeSlotName, text =>
            {
                _bakeSlotName = text ?? string.Empty;
                _armedOverwriteSlot = null;
                RefreshBakeReport();
            });
            field.gameObject.name = "BakeSlotField";

            AddHint(body, "Vacio = seed_<semilla>. Se construye en un mapa aparte: el mundo por defecto no se toca.");

            var row = MakeRow(body, "BakeRow", 30f);
            bool armed = _armedOverwriteSlot != null;
            var build = EditorUIHelpers.MakeButton(row.transform, armed ? "Seguro? Reconstruir" : "Construir mundo",
                BuildWorld, 30f, 12f);
            build.gameObject.name = "BakeButton";
            if (armed) UIButton.SetTint(build, EditorUIHelpers.DANGER);

            EditorUIHelpers.MakeButton(row.transform, "Volver al mundo base", ReturnToBaseWorld, 30f, 11f);

            _bakeReport = EditorUIHelpers.AddLabel(body, string.Empty, 10f);
            _bakeReport.color = EditorUIHelpers.TEXT_SECONDARY;
            _bakeReport.enableWordWrapping = true;
            var le = _bakeReport.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.flexibleHeight = 0f;
            RefreshBakeReport();
        }

        private void RefreshBakeReport()
        {
            if (_bakeReport == null) return;
            var request = SeedWorldBakeRequest.ForSlot(BakeSlotName);
            if (request == null) { _bakeReport.text = "Nombre no valido (vacio o 'default')."; return; }

            switch (SeedWorldBaker.Inspect(request))
            {
                case SeedWorldBaker.SlotState.Free:
                    _bakeReport.text = $"Destino: '{request.Slot}' (nuevo).";
                    break;
                case SeedWorldBaker.SlotState.SeedWorld:
                    _bakeReport.text = $"Destino: '{request.Slot}' ya existe (de Seed World): se reconstruira.";
                    break;
                default:
                    _bakeReport.text = $"'{request.Slot}' es un mapa hecho a mano: Seed World no lo sobrescribe.";
                    break;
            }
        }

        internal void BuildWorld()
        {
            if (!Application.isPlaying) { SetStatus("Construir solo funciona en Play Mode."); return; }

            var mgr = MapEditorManager.Instance;
            if (mgr == null) { SetStatus("No hay MapEditorManager en esta escena."); return; }

            var request = SeedWorldBakeRequest.ForSlot(BakeSlotName);
            if (request == null) { SetStatus("Nombre de mapa no valido (vacio o 'default')."); return; }

            var state = SeedWorldBaker.Inspect(request);
            if (state == SeedWorldBaker.SlotState.Foreign)
            {
                SetStatus($"'{request.Slot}' es un mapa hecho a mano. Elige otro nombre.");
                return;
            }

            if (state == SeedWorldBaker.SlotState.SeedWorld && _armedOverwriteSlot != request.Slot)
            {
                _armedOverwriteSlot = request.Slot;
                SetStatus($"'{request.Slot}' ya existe. Pulsa otra vez para reconstruirlo.");
                RebuildBody();
                return;
            }

            _armedOverwriteSlot = null;

            if (string.Equals(mgr.ActiveMapSlot, request.Slot, StringComparison.OrdinalIgnoreCase))
                mgr.LoadMapSlot(MapEditorMapSlots.DEFAULT_SLOT);

            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());
            var loader = FindObjectOfType<BuildingLoader>();
            var catalog = loader != null ? loader.Catalog : null;
            var result = SeedWorldBaker.Bake(_settings, request, palette, catalog);
            if (!result.Succeeded)
            {
                SetStatus("No se construyo: " + result.Error);
                RebuildBody();
                return;
            }

            var loadWatch = System.Diagnostics.Stopwatch.StartNew();
            bool loaded = mgr.LoadMapSlot(request.Slot);
            loadWatch.Stop();

            string summary =
                $"'{result.Slot}': {result.ZonesX}x{result.ZonesY} zonas, {result.Rivers} rios, " +
                $"{result.Towns} pueblos con {result.Buildings} edificios, " +
                $"{result.BlockedTiles} tiles bloqueados, {result.HardCuts} cortes sin transicion. " +
                $"Generado {result.GenerateMs} ms, escrito {result.WriteMs} ms " +
                $"({result.Bytes / (1024 * 1024f):0.0} MB), cargado {loadWatch.ElapsedMilliseconds} ms.";
            SetStatus(loaded ? summary : "Construido pero no se pudo cargar. " + summary);
            Debug.Log("[SeedWorldEditor] " + summary);
            RebuildBody();
        }

        internal void ReturnToBaseWorld()
        {
            var mgr = MapEditorManager.Instance;
            if (!Application.isPlaying || mgr == null) { SetStatus("Solo en Play Mode con el Map editor presente."); return; }
            mgr.LoadMapSlot(MapEditorMapSlots.DEFAULT_SLOT);
            SetStatus("Mundo base cargado.");
            RebuildBody();
        }
    }
}
