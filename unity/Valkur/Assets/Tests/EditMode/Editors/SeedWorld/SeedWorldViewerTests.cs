using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.SeedWorld;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Editors.SeedWorld
{
    /// <summary>
    /// "Visualizar mapa": the floating camera's arithmetic, its keys, and the refusals that keep a
    /// view from starting where it could strand the player. The trip itself needs Play Mode.
    /// </summary>
    public class SeedWorldViewerTests
    {
        private GameObject _go;
        private SeedWorldRuntimeEditor _editor;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SeedWorldViewerTest");
            _editor = _go.AddComponent<SeedWorldRuntimeEditor>();
        }

        [TearDown]
        public void TearDown()
        {
            SeedWorldLab.SetOverrideForTests(null);
            if (_editor != null && _editor.IsActive) _editor.Deactivate();
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void ADiagonal_IsNotFaster_AndOpposedKeysCancel()
        {
            Assert.AreEqual(1f, SeedWorldViewerFlight.Direction(true, false, false, true).magnitude, 1e-4f);
            Assert.AreEqual(Vector2.zero, SeedWorldViewerFlight.Direction(true, true, false, false));
            Assert.AreEqual(Vector2.left, SeedWorldViewerFlight.Direction(false, false, true, false));
        }

        [Test]
        public void Speed_IsMeasuredInScreens_SoEveryZoomCrossesTheViewInTheSameTime()
        {
            float near = SeedWorldViewerFlight.Speed(5f, false) / (2f * 5f);
            float far = SeedWorldViewerFlight.Speed(30f, false) / (2f * 30f);
            Assert.AreEqual(near, far, 1e-5f);
            Assert.AreEqual(SeedWorldViewerFlight.FastMultiplier,
                SeedWorldViewerFlight.Speed(5f, true) / SeedWorldViewerFlight.Speed(5f, false), 1e-5f);
        }

        [Test]
        public void ALongFrame_IsClamped_SoAHitchCannotThrowTheCamera()
        {
            var once = SeedWorldViewerFlight.Step(Vector2.zero, Vector2.right, 5f, false, SeedWorldViewerFlight.MaxStepSeconds);
            var hitch = SeedWorldViewerFlight.Step(Vector2.zero, Vector2.right, 5f, false, 3f);
            Assert.AreEqual(once, hitch);
            Assert.AreEqual(Vector2.one, SeedWorldViewerFlight.Step(Vector2.one, Vector2.zero, 5f, true, 0.05f));
        }

        [Test]
        public void TheCentre_StaysOverTheWorld_AndAnUnknownWorldClampsNothing()
        {
            var world = new Rect(100f, 50f, 400f, 400f);
            Assert.AreEqual(new Vector2(100f, 450f), SeedWorldViewerFlight.ClampToWorld(new Vector2(-9f, 900f), world));
            Assert.AreEqual(new Vector2(-9f, 900f), SeedWorldViewerFlight.ClampToWorld(new Vector2(-9f, 900f), default));
        }

        [Test]
        public void TheFlightKeys_AreDeclared_AndOwnedByTheEditorsExactName()
        {
            var tools = InputActionCatalog.All.Where(d => d.Map == InputActionCatalog.MapSeedWorldEditor).ToList();
            foreach (var action in new[] { "FlyUp", "FlyDown", "FlyLeft", "FlyRight" })
            {
                var d = tools.FirstOrDefault(t => t.Action == action);
                Assert.IsNotNull(d, $"'{action}' is missing from the catalogue.");
                Assert.AreEqual(_editor.EditorName, d.OwnerEditor,
                    "OwnerEditor is compared EXACTLY with the EditorName; a mismatch kills the tool silently.");
            }
        }

        [Test]
        public void EveryFlightKey_IsReadByTheViewer()
        {
            string path = Directory.GetFiles(Application.dataPath, "SeedWorldRuntimeEditor.Viewer.cs", SearchOption.AllDirectories).Single();
            string src = File.ReadAllText(path);
            foreach (var action in new[] { "FlyUp", "FlyDown", "FlyLeft", "FlyRight" })
                StringAssert.Contains($"EditorInput.ToolHeld(MapSeedWorld, \"{action}\")", src);
            StringAssert.Contains("KeyboardInputManager.IsShiftHeld()", src);
            StringAssert.Contains("EscapeOwnership.Claim(this)", src);
            StringAssert.Contains("EscapeOwnership.Release(this)", src);
        }

        [Test]
        public void TheView_IsRefusedOutsidePlayMode_AndWithTheLabOff()
        {
            _editor.Activate();
            Assert.IsNotNull(_editor.ViewRefusal(), "Edit Mode has no world to load.");

            SeedWorldLab.SetOverrideForTests(false);
            _editor.BeginViewing();
            Assert.IsFalse(_editor.IsViewing);
        }

        [Test]
        public void ThePreviewPanel_OffersTheView_AndTheButtonFollowsTheLab()
        {
            SeedWorldLab.SetOverrideForTests(false);
            _editor.Activate();
            var button = _go.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "ViewMapButton");
            Assert.IsNotNull(button);
            Assert.IsFalse(button.interactable, "With the lab off the view cannot build.");
        }

        [Test]
        public void TheHelpLine_UsesOnlyGlyphsTheFontHas()
        {
            foreach (char c in SeedWorldRuntimeEditor.ViewHelpText)
                Assert.Less((int)c, 128, $"'{c}' is not ASCII; LiberationSans draws a box for most symbols.");
        }
    }
}
