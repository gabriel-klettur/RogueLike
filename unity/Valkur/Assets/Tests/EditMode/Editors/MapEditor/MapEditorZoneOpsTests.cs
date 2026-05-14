using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// End-to-end zone CRUD operations: Rename, ToggleEditable, Move, Duplicate,
    /// Delete, Add Zone flow, and the Restrict tile-editing flag.
    /// Wires a real MapEditorManager + ZoneManager pair via reflection
    /// (no UI, no disk outside Application.persistentDataPath sandbox).
    /// </summary>
    [TestFixture]
    public class MapEditorZoneOpsTests : MapEditorTestBase
    {
        // ── Rename ────────────────────────────────────────────────────────────────

        [Test]
        public void Operation_RenameSelectedZone_RenamesInZoneManager()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RenameSelectedZone", "Gamma");

            Assert.IsFalse(GetZM(mgr).TryGetZone("Alpha", out _),
                "'Alpha' must no longer exist after rename.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Gamma", out _),
                "'Gamma' must exist after rename.");
            Assert.AreEqual("Gamma", GetState(mgr).SelectedZone,
                "Selection must follow the renamed zone.");
        }

        [Test]
        public void Operation_RenameSelectedZone_NoSelection_DoesNotThrow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            Assert.DoesNotThrow(() => InvokeMethod(mgr, "RenameSelectedZone", "Foo"),
                "Rename without a selection must be a safe no-op.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Original zone must remain intact when rename has no selection.");
        }

        [Test]
        public void Operation_RenameSelectedZone_EmptyName_DoesNotRename()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RenameSelectedZone", "   ");

            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Empty rename input must not modify the zone name.");
        }

        // ── Toggle Editable ───────────────────────────────────────────────────────

        [Test]
        public void Operation_ToggleSelectedZoneEditable_FlipsFlag()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "ToggleSelectedZoneEditable");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z1));
            Assert.IsFalse(z1.editableInTileEditor, "Editable flag must flip true→false.");

            InvokeMethod(mgr, "ToggleSelectedZoneEditable");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z2));
            Assert.IsTrue(z2.editableInTileEditor, "Editable flag must flip false→true.");
        }

        // ── Move ──────────────────────────────────────────────────────────────────

        [Test]
        public void Operation_MoveSelectedZone_AppliesZoneStridedDelta()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");
            int w = GetZM(mgr).ZoneWidthTiles;
            int h = GetZM(mgr).ZoneHeightTiles;

            InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.right);
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var moved1));
            Assert.AreEqual(new Vector2Int(w, 0), moved1.gridOffset,
                "Move right must shift by ZoneWidthTiles, not 1.");

            InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.up);
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var moved2));
            Assert.AreEqual(new Vector2Int(w, h), moved2.gridOffset,
                "Move up must add ZoneHeightTiles to Y.");
        }

        [Test]
        public void Operation_MoveSelectedZone_NoSelection_DoesNotThrow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            Assert.DoesNotThrow(() =>
                InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.right));
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z));
            Assert.AreEqual(Vector2Int.zero, z.gridOffset,
                "Zone offset must not change when no selection.");
        }

        // ── Duplicate ─────────────────────────────────────────────────────────────

        [Test]
        public void Operation_DuplicateSelectedZone_CreatesShiftedCopy()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            int before = GetZM(mgr).GetZonesSnapshot().Length;
            InvokeMethod(mgr, "DuplicateSelectedZone");
            int after = GetZM(mgr).GetZonesSnapshot().Length;

            Assert.AreEqual(before + 1, after, "Duplicate must add exactly one zone.");
            Assert.AreNotEqual("Alpha", GetState(mgr).SelectedZone,
                "Selection must follow the new duplicate, not the source.");

            Assert.IsTrue(GetZM(mgr).TryGetZone(GetState(mgr).SelectedZone, out var dup));
            Assert.AreEqual(new Vector2Int(GetZM(mgr).ZoneWidthTiles, 0), dup.gridOffset,
                "Duplicate must be shifted right by ZoneWidthTiles to avoid overlap.");
        }

        // ── Delete (request + confirm) ────────────────────────────────────────────

        [Test]
        public void Operation_RequestDelete_StoresPendingDeleteName()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Beta");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            var pending = (string) GetFieldValue(mgr, "_pendingDeleteZoneName");
            Assert.AreEqual("Beta", pending,
                "RequestDelete must stage the selected zone name for confirmation.");
        }

        [Test]
        public void Operation_ConfirmDelete_RemovesPendingZone()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Beta");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            InvokeMethod(mgr, "ConfirmDeleteSelectedZone");

            Assert.IsFalse(GetZM(mgr).TryGetZone("Beta", out _),
                "'Beta' must be removed after Confirm.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Other zones must remain.");
            Assert.IsNull(GetFieldValue(mgr, "_pendingDeleteZoneName"),
                "_pendingDeleteZoneName must clear after confirm.");
        }

        [Test]
        public void Operation_ConfirmDelete_LastZone_RefusesToDelete()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            InvokeMethod(mgr, "ConfirmDeleteSelectedZone");

            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Cannot delete the last remaining zone — must refuse.");
        }

        // ── Add Zone Flow ─────────────────────────────────────────────────────────

        [Test]
        public void Operation_BeginAddZoneFlow_NoSelection_StillActivatesFlow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            InvokeMethod(mgr, "BeginAddZoneFlow");

            Assert.IsTrue((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "Add Zone flow must activate even without a pre-selection — source zone is optional.");
        }

        [Test]
        public void Operation_ConfirmAddZone_FromTemplate_AppendsZoneAtTarget()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);
            SetField(mgr, "_pendingAddZoneOffset", new Vector2Int(50, 0));

            InvokeMethod(mgr, "ConfirmAddZone", "Beta", true, false);

            Assert.IsTrue(GetZM(mgr).TryGetZone("Beta", out var beta),
                "ConfirmAddZone must add the new zone via template path.");
            Assert.AreEqual(new Vector2Int(50, 0), beta.gridOffset);
            Assert.IsFalse(beta.editableInTileEditor,
                "Editable override (false) must be applied to the new zone.");
            Assert.AreEqual("Beta", GetState(mgr).SelectedZone,
                "New zone must become the selection after confirm.");
            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "Flow must end after a successful confirm.");
        }

        [Test]
        public void Operation_ConfirmAddZone_WithoutTarget_DoesNotCreateZone()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", false);

            InvokeMethod(mgr, "ConfirmAddZone", "Beta", true, true);

            Assert.IsFalse(GetZM(mgr).TryGetZone("Beta", out _),
                "ConfirmAddZone must refuse when no target offset has been marked.");
        }

        [Test]
        public void Operation_ConfirmAddZone_EmptyName_DoesNotCreateZone()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);
            SetField(mgr, "_pendingAddZoneOffset", new Vector2Int(50, 0));

            int before = GetZM(mgr).GetZonesSnapshot().Length;
            InvokeMethod(mgr, "ConfirmAddZone", "   ", true, true);
            int after = GetZM(mgr).GetZonesSnapshot().Length;

            Assert.AreEqual(before, after,
                "Empty / whitespace zone name must be rejected by Confirm.");
        }

        [Test]
        public void Operation_CancelAddZoneFlow_ResetsFlowFlags()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);

            InvokeMethod(mgr, "CancelAddZoneFlow");

            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"));
            Assert.IsFalse((bool) GetFieldValue(mgr, "_hasPendingAddTarget"));
        }

        // ── SetRestrictTileEditing ────────────────────────────────────────────────

        [Test]
        public void Operation_SetRestrictTileEditing_PersistsFlag()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            var st  = GetState(mgr);

            InvokeMethod(mgr, "SetRestrictTileEditing", false);
            Assert.IsFalse(st.RestrictTileEditingToEditableZones);

            InvokeMethod(mgr, "SetRestrictTileEditing", true);
            Assert.IsTrue(st.RestrictTileEditingToEditableZones);
        }

        // ── Adaptive overlay-line width ────────────────────────────────────────────

        [Test]
        public void Overlay_ComputeAdaptiveLineWidth_ScalesWithCameraZoom()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));

            var camGo = new GameObject("OpsTestCam");
            _sceneObjects.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            SetField(mgr, "_mainCamera", cam);

            float wClose = (float) typeof(MapEditorManager)
                .GetMethod("ComputeAdaptiveLineWidth",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(mgr, null);

            cam.orthographicSize = 50f;
            float wFar = (float) typeof(MapEditorManager)
                .GetMethod("ComputeAdaptiveLineWidth",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(mgr, null);

            Assert.GreaterOrEqual(wFar, wClose,
                "Adaptive line width must grow (or stay equal at clamp) when zooming out.");
            Assert.GreaterOrEqual(wClose, 0.01f,
                "Adaptive line width must stay strictly positive.");
        }
    }
}
