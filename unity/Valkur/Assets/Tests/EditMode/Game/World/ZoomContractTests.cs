using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Architectural contract test for camera zoom in Valkur.
    ///
    /// Bug history (do not regress):
    ///   - 2026-05-16 #1: CameraPixelSnap was modified to quantise
    ///     <c>orthographicSize</c> each frame so 1/wpp would be integer
    ///     (fixing chunk-boundary seams). This broke gameplay zoom and the
    ///     zoom in every in-game editor (Tile, Buildings, Map, …) because
    ///     each editor's <see cref="Valkur.Gameplay.CameraSetup.SetEditorZoom"/>
    ///     was instantly overwritten by the snap.
    ///   - 2026-05-16 #2: A second iteration tried snapping ortho to the
    ///     nearest multiple of the tile PPU. Worse — discrete zoom levels
    ///     made the gameplay zoom feel broken AND it didn't fix the seams.
    ///
    /// The architectural invariant this test pins:
    ///   Only a small, named set of callsites is allowed to assign
    ///   <c>orthographicSize</c> or <c>m_Lens.OrthographicSize</c>. Anyone
    ///   else who writes to those properties is breaking the zoom contract,
    ///   and this test fails immediately to surface the regression.
    ///
    /// To intentionally add a new authorised callsite:
    ///   1. Confirm it doesn't run every frame (don't fight Cinemachine).
    ///   2. Add the file's path to <see cref="AuthorisedCallsites"/> below.
    /// </summary>
    [TestFixture]
    public class ZoomContractTests
    {
        // ── Authorised callsites that may write orthographicSize ────────────
        //
        // Each entry is a path fragment matched against the file's full
        // path with OrdinalIgnoreCase. Keeping the whitelist tight is the
        // entire point of this test — every extra entry weakens the guard.

        private static readonly string[] AuthorisedCallsites =
        {
            // The central zoom owner. SetEditorZoom() clamps and gates every
            // editor's zoom request before writing the lens; Update() does
            // the gameplay clamp; Awake() reads the serialised default.
            "Gameplay/World/Setup/CameraSetup.cs",

            // Main menu spawns its own camera and sets a fixed framing
            // ortho — runs once at scene load, not every frame.
            "UI/MainMenu/MainMenuUI.cs",

            // Off-screen preview cameras for the Spells / Particles
            // editors. They render into a RenderTexture, not the game
            // view, so their ortho is independent of gameplay zoom.
            "Gameplay/Editors/Spells/SpellPreviewService.cs",
            "Gameplay/Editors/Spells/SpellPreviewService.Framing.cs",
            "Gameplay/Editors/Particles/ParticlePreviewService.cs",

            // Inspector preview for a ParticlePresetDefinition. The camera
            // belongs to a PreviewRenderUtility and lives in its own preview
            // scene — it is not the game camera and Cinemachine never sees it.
            // Written once per preset change, not per repaint.
            "Editor/Windows/ParticlePresetDefinitionEditor.cs",

            // Tile editor zoom input → routed back through CameraSetup,
            // but historically also held a fallback path. Allowed; covered
            // by separate CameraZoomClampTests.
            "Gameplay/Editors/Tile/TileEditorManager.InputHandlers.cs",
        };

        // Matches both direct `cam.orthographicSize = …` and
        // `vcam.m_Lens.OrthographicSize = …` assignments. Excludes reads.
        private static readonly Regex OrthoWritePattern = new Regex(
            @"\b(?:orthographicSize|OrthographicSize)\s*=(?!=)",
            RegexOptions.Compiled);

        private static string ScriptsRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Scripts"));

        private static IEnumerable<string> EnumerateScripts()
        {
            return Directory.EnumerateFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories);
        }

        private static bool IsAuthorised(string fullPath)
        {
            string normalised = fullPath.Replace('\\', '/');
            foreach (var allowed in AuthorisedCallsites)
            {
                string allowedNorm = allowed.Replace('\\', '/');
                if (normalised.EndsWith(allowedNorm, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // Hard invariant — orthographicSize writes are gated by the whitelist
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void OrthographicSize_IsOnlyWrittenByAuthorisedCallsites()
        {
            Assert.That(Directory.Exists(ScriptsRoot),
                $"Scripts root not found at '{ScriptsRoot}'.");

            var violators = new List<string>();
            foreach (var path in EnumerateScripts())
            {
                if (IsAuthorised(path)) continue;

                string text = File.ReadAllText(path);
                // Strip both single-line and block comments BEFORE matching so
                // documentation that mentions "orthographicSize = …" doesn't
                // create false positives.
                string stripped = StripComments(text);
                if (!OrthoWritePattern.IsMatch(stripped)) continue;

                // Found an unauthorised write — record the line(s).
                int lineNo = 1;
                foreach (var line in stripped.Split('\n'))
                {
                    if (OrthoWritePattern.IsMatch(line))
                        violators.Add($"  {path}:{lineNo}  →  {line.Trim()}");
                    lineNo++;
                }
            }

            Assert.That(violators.Count, Is.EqualTo(0),
                "Unauthorised write(s) to orthographicSize detected. The zoom " +
                "contract requires that only CameraSetup / preview services / " +
                "MainMenuUI write the camera lens. Per-frame writes elsewhere " +
                "(especially in CameraPixelSnap or any LateUpdate component) " +
                "fight Cinemachine and break editor zoom.\n\n" +
                "If you intentionally need a new callsite, add its path to " +
                "ZoomContractTests.AuthorisedCallsites and document why it " +
                "doesn't run every frame.\n\n" +
                "Violations:\n" + string.Join("\n", violators));
        }

        // ────────────────────────────────────────────────────────────────────
        // Hard invariant — CameraPixelSnap stays POSITION-ONLY
        //
        // This is a focused source-level check for the file most likely to
        // regress (it ran in LateUpdate and was twice modified to touch ortho
        // size, both times breaking zoom UX project-wide).
        // ────────────────────────────────────────────────────────────────────

        [Test]
        public void CameraPixelSnap_DoesNotMutateOrthographicSize()
        {
            string path = Path.Combine(ScriptsRoot,
                "Gameplay", "World", "Setup", "CameraPixelSnap.cs");
            Assert.That(File.Exists(path),
                $"CameraPixelSnap.cs not found at '{path}'.");

            string stripped = StripComments(File.ReadAllText(path));
            Assert.That(OrthoWritePattern.IsMatch(stripped), Is.False,
                "CameraPixelSnap must NEVER write to orthographicSize. It runs " +
                "after Cinemachine every LateUpdate; any write here would " +
                "overwrite the user's gameplay zoom AND every editor's " +
                "SetEditorZoom call. Rewriting the lens has broken zoom UX " +
                "TWICE before — the position snap is the only legitimate job " +
                "for this component.\n\n" +
                "If you need to fix tilemap seams, do it at the renderer or " +
                "asset-import layer (FullRect mesh, atlas extrude, integer-pixel " +
                "viewport in AspectRatioEnforcer), not by hijacking the camera lens.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Strips both // line comments and / * block * / comments from text
        // so the regex above doesn't trip on documentation.
        // ────────────────────────────────────────────────────────────────────

        private static string StripComments(string src)
        {
            // Block comments first (greedy = simplest, no nesting in C#).
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            // Then line comments — match `//` to end of line.
            string noLine = Regex.Replace(noBlock, @"//.*?$", "", RegexOptions.Multiline);
            return noLine;
        }
    }
}
