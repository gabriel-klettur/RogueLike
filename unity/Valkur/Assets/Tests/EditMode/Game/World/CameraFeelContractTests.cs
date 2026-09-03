using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Source-level guards on the camera layer, in the same style as
    /// <c>CameraOrthoSnapTests.SourceCode_WritePointsRouteThroughSnap</c>.
    ///
    /// These pin the four properties the whole design rests on, none of which can be
    /// expressed as a runtime assertion: that the director never writes a lens, never writes
    /// the render camera, never detaches the follow target, and never runs on scaled time.
    /// Violating any of them produces a camera that looks fine in the Editor and is wrong in
    /// a real session.
    /// </summary>
    [TestFixture]
    public class CameraFeelContractTests
    {
        private static string ScriptsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string CameraDir =>
            Path.Combine(ScriptsRoot, "Gameplay", "World", "Camera");

        private static IEnumerable<(string path, string body)> CameraSources()
        {
            foreach (var file in Directory.GetFiles(CameraDir, "*.cs", SearchOption.AllDirectories))
                yield return (file, StripComments(File.ReadAllText(file)));
        }

        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return string.Join("\n", src.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("///")));
        }

        private static string Read(string relative)
        {
            string path = Path.Combine(ScriptsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), $"Expected file missing: {relative}");
            return StripComments(File.ReadAllText(path));
        }

        [Test]
        public void CameraDirectoryExists()
            => Assert.IsTrue(Directory.Exists(CameraDir),
                "Without this the whole fixture silently scans nothing.");

        [Test]
        public void CameraFeel_NeverWritesAnyLens()
        {
            var offenders = new List<string>();
            var write = new Regex(@"\b[Oo]rthographicSize\s*[*/+\-]?=");

            foreach (var (path, body) in CameraSources())
            {
                if (write.IsMatch(body)) offenders.Add($"{Path.GetFileName(path)}: writes orthographicSize");
                if (body.Contains("m_Lens")) offenders.Add($"{Path.GetFileName(path)}: touches m_Lens");
            }

            Assert.IsEmpty(offenders,
                "CameraPixelSnap derives its lattice from the live orthographic size, and " +
                "CameraSetup keeps that size on a ladder where one art texel is an integer " +
                "number of screen pixels. Any lens write lands between rungs and makes every " +
                "tile on screen crawl.\n\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void CameraFeel_NeverWritesTheRenderCameraTransform()
        {
            var offenders = new List<string>();
            var main = new Regex(@"Camera\s*\.\s*main\s*\.\s*transform");
            var cached = new Regex(@"_renderCam\s*\.\s*transform\s*\.\s*position\s*=");

            foreach (var (path, body) in CameraSources())
                if (main.IsMatch(body) || cached.IsMatch(body))
                    offenders.Add(Path.GetFileName(path));

            Assert.IsEmpty(offenders,
                "Writing the camera transform means racing the Cinemachine brain for it, " +
                "which the old CameraShake did and lost — its restore subtracted an offset " +
                "the brain had already erased. The director moves the follow proxy.\n\n  " +
                string.Join("\n  ", offenders));
        }

        [Test]
        public void CameraFeel_NeverDetachesTheFollowTarget()
        {
            var offenders = CameraSources()
                .Where(s => s.body.Contains("DetachFollow"))
                .Select(s => Path.GetFileName(s.path))
                .ToList();

            Assert.IsEmpty(offenders,
                "CameraSetup runs an auto-reattach watchdog that would undo it every frame.\n\n  " +
                string.Join("\n  ", offenders));
        }

        [Test]
        public void CameraFeelDirector_RunsOnUnscaledTimeOnly()
        {
            string body = Read("Gameplay/World/Camera/CameraFeelDirector.cs");

            Assert.IsTrue(body.Contains("Time.unscaledDeltaTime"),
                "The solver must tick on unscaled time.");
            Assert.IsFalse(Regex.IsMatch(body, @"Time\s*\.\s*deltaTime"),
                "Hit-stop drops the scaled clock to six percent. The shake continuing at full " +
                "speed through the freeze is what makes the freeze read as impact instead of " +
                "as a dropped frame.");
        }

        [Test]
        public void CameraFeelDirector_PollsTheEditorGateRatherThanSubscribing()
        {
            string body = Read("Gameplay/World/Camera/CameraFeelDirector.cs");

            Assert.IsTrue(body.Contains("AnyEditorActive"),
                "The director must suppress itself while a runtime editor is open.");
            Assert.IsFalse(body.Contains("OnEditorStateChanged"),
                "GameEditorManager.Unregister clears the active editor WITHOUT firing that " +
                "event, so a subscriber is left believing an editor is still open forever.");
        }

        [Test]
        public void CameraFeelDirector_ForcesTheBrainOntoTheRenderClock()
        {
            string body = Read("Gameplay/World/Camera/CameraFeelDirector.cs");

            Assert.IsTrue(body.Contains("CinemachineBrain.UpdateMethod.LateUpdate"),
                "The scene ships SmartUpdate, which can settle on the physics clock and " +
                "evaluate the camera at three hertz during hit-stop.");
            Assert.IsTrue(body.Contains("m_IgnoreTimeScale = true"),
                "Otherwise any delta Cinemachine derives is scaled too.");
        }

        [Test]
        public void CameraFeelDirector_DeclaresItsOwnSubsystemReset()
        {
            string body = Read("Gameplay/World/Camera/CameraFeelDirector.Events.cs");
            Assert.IsTrue(body.Contains("RuntimeInitializeLoadType.SubsystemRegistration"),
                "DomainReloadStaticResetTests scans with DeclaredOnly, so inheriting the " +
                "singleton base's hook is not enough for the static boss set.");
        }

        [Test]
        public void TheOldCameraShakeIsGone()
        {
            // The class used to live inside FireballImpactFX.cs, which is gone. Scanning
            // the whole tree instead of that one file keeps the guarantee alive without
            // pinning it to a filename: its amplitude ratcheted upward for the life of
            // the session and its restore subtracted an offset the brain had already
            // erased, so it must not come back anywhere.
            var declarations = Directory
                .GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => StripComments(File.ReadAllText(f)).Contains("class CameraShake"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.IsEmpty(declarations,
                "CameraFeelDirector is the only shake owner.\n\n  " +
                string.Join("\n  ", declarations));

            var survivors = Directory
                .GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => StripComments(File.ReadAllText(f)).Contains("CameraShake.Trigger"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.IsEmpty(survivors,
                "Call sites must go through CameraFeel.Cue so the rate limit, the trauma " +
                "budget and the master intensity cannot be forgotten.\n\n  " +
                string.Join("\n  ", survivors));
        }

        [Test]
        public void TheFacadeIsNullSafe()
        {
            string body = Read("Gameplay/World/Camera/CameraFeel.cs");

            Assert.IsTrue(body.Contains("HasInstance"),
                "Every entry point must be a no-op without a director — EditMode, boot and " +
                "the spell-preview scene all run without one.");
            Assert.IsFalse(body.Contains("Instance?."),
                "SingletonMonoBehaviour.Instance returns the raw backing field with no Unity " +
                "null coercion, so with Domain Reload off a destroyed director survives the " +
                "C# null check and throws on use.");
        }

        [Test]
        public void TheCameraEditorCarriesTheStandardEditorChrome()
        {
            string builder = Read("Gameplay/Editors/Camera/CameraEditorUIBuilder.cs");

            foreach (var piece in new[] { "BuildMenuBar", "BuildStatusBar", "AllPanels" })
                Assert.IsTrue(builder.Contains(piece),
                    $"The Camera Editor is missing {piece}. Every runtime editor shares one " +
                    "chrome so the player learns the layout once.");

            string panels = Read("Gameplay/Editors/Camera/CameraRuntimeEditor.Panels.cs");
            foreach (var piece in new[] { "BuildTutorial", "TogglePanel", "OpenAllPanels",
                                          "private void Undo", "private void Redo" })
                Assert.IsTrue(panels.Contains(piece),
                    $"The Camera Editor is missing {piece}.");
        }

        [Test]
        public void EveryPanelHasAMenuButtonAndViceVersa()
        {
            string builder = Read("Gameplay/Editors/Camera/CameraEditorUIBuilder.cs");

            var declared = Regex.Matches(builder, "PANEL_([A-Z]+)" + @"\s*=\s*" + "\"")
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.IsNotEmpty(declared, "No panel ids found — the scan is broken, not the code.");

            // The AllPanels initialiser, isolated, so the check does not depend on how the
            // declaration happens to be wrapped.
            int open = builder.IndexOf("AllPanels", System.StringComparison.Ordinal);
            Assert.Greater(open, -1, "AllPanels is gone; nothing could open every panel.");
            int brace = builder.IndexOf('{', open);
            int close = builder.IndexOf("};", brace, System.StringComparison.Ordinal);
            Assert.Greater(close, brace, "AllPanels initialiser is malformed.");
            string allPanelsBlock = builder.Substring(brace, close - brace);

            var missingFromList = declared
                .Where(id => !allPanelsBlock.Contains("PANEL_" + id))
                .ToList();
            Assert.IsEmpty(missingFromList,
                "These panels are not in AllPanels, so opening the editor would leave them " +
                "hidden with no way to know they exist: " +
                string.Join(", ", missingFromList.Select(id => "PANEL_" + id)));

            var missingButton = declared
                .Where(id => !Regex.IsMatch(builder, @"AddMenuButton\([^;]*PANEL_" + id))
                .ToList();
            Assert.IsEmpty(missingButton,
                "These panels have no menu-bar button, so once closed they cannot be " +
                "reopened: " +
                string.Join(", ", missingButton.Select(id => "PANEL_" + id)));
        }

        [Test]
        public void TheCameraEditorNeverWritesTheProfileWithoutRecordingIt()
        {
            string bindings = Read("Gameplay/Editors/Camera/CameraRuntimeEditor.Bindings.cs");

            // Every SetTunable/SetCue driven by a slider must be paired with a PushEdit, or
            // Undo walks a history that does not match what the profile actually holds.
            int writes = Regex.Matches(bindings, @"_profile\.Set(Tunable|Cue)\(").Count;
            int recorded = Regex.Matches(bindings, @"PushEdit\(").Count;

            Assert.GreaterOrEqual(recorded, 2,
                "Slider edits must be recorded for Undo.");
            Assert.LessOrEqual(writes - recorded, 1,
                $"{writes} profile writes but only {recorded} undo entries. An unrecorded " +
                "write makes Undo restore a value the profile never had.");
        }

        [Test]
        public void TheCameraEditorIsReachableFromTheGeneralEditor()
        {
            string registry = Read("Gameplay/Editors/General/GeneralEditorRegistry.cs");
            Assert.IsTrue(registry.Contains("CameraRuntimeEditor"),
                "The Camera Editor carries no hotkey by design, so the General Editor entry " +
                "is the ONLY way to open it. Losing that line makes it unreachable with " +
                "nothing to notice.");

            string bootstrap = Read("Gameplay/Bootstrap/GameplaySceneSetup.cs");
            Assert.IsTrue(bootstrap.Contains("EnsureCameraEditor"),
                "...and it must actually be created in the scene.");
        }
    }
}
