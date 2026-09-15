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

        /// <summary>
        /// Build a LIVE world (ground generated around the player, nothing on disk until edited)
        /// or bake every zone to a file. Live is the default since phase 5: kilobytes instead of
        /// megabytes and no freeze; baked stays for a world meant to be hand-finished zone by zone.
        /// </summary>
        private bool _buildLive = true;

        internal bool BuildLive
        {
            get => _buildLive;
            set { _buildLive = value; RebuildBody(); }
        }
        private TextMeshProUGUI _bakeReport;

        /// <summary>The slot a bake targets: the typed name, or <c>seed_&lt;seed&gt;</c> when empty.</summary>
        internal string BakeSlotName
            => string.IsNullOrWhiteSpace(_bakeSlotName) ? $"seed_{_settings.seed}" : _bakeSlotName.Trim();

        private void BuildBakeSection(Transform body)
        {
            EditorUIHelpers.BuildSectionHeader(body, "Construir");

            // The lab switch sits ABOVE everything that builds: with it off the game neither builds
            // nor enters a generated world, and the preview above keeps working because it writes
            // nothing. Turning it off never strands anyone — the return button stays live.
            var labRow = MakeRow(body, "LabRow", ROW_H + 4f);
            AddCaption(labRow.transform, "Laboratorio");
            bool lab = SeedWorldLab.Enabled;
            var labButton = EditorUIHelpers.MakeButton(labRow.transform, lab ? "ENCENDIDO" : "APAGADO",
                ToggleLab, ROW_H, 11f);
            labButton.gameObject.name = "LabButton";
            if (!lab) UIButton.SetTint(labButton, EditorUIHelpers.DANGER_IDLE);
            AddHint(body, lab
                ? "Encendido en esta maquina: Construir lleva al jugador a un mundo generado y la vuelta le deja donde estaba en Pepitoria."
                : "Apagado: Seed World queda separado del juego (solo vista previa). Enciendelo para construir y entrar.");

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

            var modeRow = MakeRow(body, "BakeModeRow", ROW_H + 4f);
            AddCaption(modeRow.transform, "Modo");
            var liveButton = EditorUIHelpers.MakeButton(modeRow.transform, _buildLive ? "EN VIVO" : "HORNEADO",
                () => BuildLive = !_buildLive, ROW_H, 11f);
            liveButton.gameObject.name = "BakeModeButton";
            AddHint(body, _buildLive
                ? "En vivo: el suelo se genera alrededor del jugador y solo se guarda lo que edites."
                : "Horneado: cada zona se escribe a disco (megas en mundos grandes, se puede retocar todo).");

            var row = MakeRow(body, "BakeRow", 30f);
            bool armed = _armedOverwriteSlot != null;
            var build = EditorUIHelpers.MakeButton(row.transform, armed ? "Seguro? Reconstruir" : "Construir mundo",
                BuildWorld, 30f, 12f);
            build.gameObject.name = "BakeButton";
            if (armed) UIButton.SetTint(build, EditorUIHelpers.DANGER);
            // Clickable even with the lab off: BuildWorld answers with the reason. A disabled
            // button here drew like an enabled one and swallowed the click in silence.
            if (!lab) UIButton.SetTint(build, EditorUIHelpers.BTN_NORMAL);

            var back = EditorUIHelpers.MakeButton(row.transform, "Volver a Pepitoria", ReturnToBaseWorld, 30f, 11f);
            back.gameObject.name = "ReturnHomeButton";

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
            if (!SeedWorldLab.Enabled) { SetStatus(SeedWorldLab.OffMessage); return; }
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

            var outcome = SeedWorldLauncher.BuildAndLoad(_settings, request.Slot, _buildLive);
            string summary = SeedWorldLauncher.Describe(outcome);
            SetStatus(summary);
            if (outcome.Succeeded) Debug.Log("[SeedWorldEditor] " + summary);
            RebuildBody();
        }

        internal void ReturnToBaseWorld()
        {
            SetStatus(SeedWorldLauncher.ReturnHome());
            RebuildBody();
        }
    }
}
