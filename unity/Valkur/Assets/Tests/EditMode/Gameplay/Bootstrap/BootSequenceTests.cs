using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Gameplay.Bootstrap
{
    /// <summary>
    /// The boot sequence, now that it is a value rather than a 250-line coroutine.
    ///
    /// <para>What this fixture is FOR is the half that used to be untestable. The old
    /// <c>Start</c> carried at least six ordering constraints, every one of them
    /// documented in a comment and exactly one of them checked — by a test that ran
    /// <c>IndexOf</c> over the source text looking for two method names. Everything
    /// else (the editor manager before any editor, the workspace service before any
    /// panel, the world-damage service before the building loader) was protected by
    /// nothing but the order somebody happened to type.</para>
    ///
    /// <para>Building the sequence is pure composition — no step runs — so the whole
    /// order can be walked in EditMode without a world.</para>
    /// </summary>
    [TestFixture]
    public class BootSequenceTests
    {
        private GameObject _go;
        private List<BootStep> _steps;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("BootSequenceProbe");
            var setup = _go.AddComponent<GameplaySceneSetup>();
            var mi = typeof(GameplaySceneSetup).GetMethod("BuildBootSequence",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "BuildBootSequence ha desaparecido.");
            _steps = (List<BootStep>)mi.Invoke(setup, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private int IndexOfLabel(string label)
        {
            for (int i = 0; i < _steps.Count; i++)
                if (_steps[i].Label == label) return i;
            return -1;
        }

        private void AssertBefore(string first, string second)
        {
            int a = IndexOfLabel(first), b = IndexOfLabel(second);
            Assert.AreNotEqual(-1, a, $"Falta la etapa '{first}'.");
            Assert.AreNotEqual(-1, b, $"Falta la etapa '{second}'.");
            Assert.Less(a, b, $"'{first}' debe ejecutarse ANTES que '{second}'.");
        }

        // ── Etapas ───────────────────────────────────────────────────────────

        /// <summary>
        /// Each etapa is one segment of the loading bar and one series in
        /// <c>Diagnostics/Boot/boot_phases.csv</c>. A name that appeared twice with other
        /// etapas in between would draw two segments with one label and split one series
        /// into two halves that the trend report would sum as if they were one.
        /// </summary>
        [Test]
        public void EveryEtapaIsOneContiguousRun_AndNoStepIsLeftUnphased()
        {
            var seen = new HashSet<string>();
            string previous = null;
            foreach (var s in _steps)
            {
                Assert.AreNotEqual(BootPlan.DefaultPhase, s.Phase,
                    $"El paso '{s.Label}' no pertenece a ninguna etapa declarada.");
                if (s.Phase == previous) continue;
                Assert.IsTrue(seen.Add(s.Phase),
                    $"La etapa '{s.Phase}' aparece dos veces separada por otras: seria dos segmentos de la barra.");
                previous = s.Phase;
            }
            Assert.GreaterOrEqual(seen.Count, 6, "La barra deberia dividirse en varias etapas.");
        }

        [Test]
        public void EtapaNamesArePlainAscii()
        {
            foreach (var s in _steps)
                foreach (char c in s.Phase)
                    Assert.Less((int)c, 128, $"Caracter no ASCII en la etapa '{s.Phase}': es una clave de CSV.");
        }

        // ── Shape ────────────────────────────────────────────────────────────

        [Test]
        public void SequenceIsNotEmpty_AndEveryStepHasABody()
        {
            Assert.Greater(_steps.Count, 40, "La secuencia de arranque se ha quedado corta.");
            foreach (var s in _steps)
                Assert.IsTrue(s.Run != null || s.Progressive != null,
                    $"El paso '{s.Label}' no ejecuta nada.");
        }

        [Test]
        public void EveryReportedLabelIsUnique()
        {
            var seen = new HashSet<string>();
            foreach (var s in _steps)
            {
                if (!s.IsReported) continue;
                Assert.IsTrue(seen.Add(s.Label),
                    $"Dos pasos comparten la etiqueta '{s.Label}'. Los pesos se guardan por " +
                    "etiqueta, asi que un duplicado calibra la barra contra la etapa equivocada.");
            }
        }

        [Test]
        public void EveryStepCarriesPositiveWeight()
        {
            foreach (var s in _steps)
                Assert.Greater(s.Weight, 0f, $"El paso '{s.Label}' pesa cero.");
        }

        /// <summary>
        /// The bug this whole layer exists to make impossible. A constant naming the
        /// number of steps is a second copy of something the list already knows, and
        /// the shipped one had drifted by seventeen.
        /// </summary>
        [Test]
        public void NoHandMaintainedStepTotalSurvives()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Bootstrap");
            foreach (var file in Directory.GetFiles(dir, "GameplaySceneSetup*.cs"))
            {
                // Comments may NAME the retired constant - this file's own docs do -
                // and a scan that cannot tell prose from code fails on its own
                // explanation of why the constant is gone.
                var code = new System.Text.StringBuilder();
                foreach (var line in File.ReadAllLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    code.AppendLine(line);
                }
                string src = code.ToString();
                Assert.IsFalse(src.Contains("SetupStepTotal"),
                    $"{Path.GetFileName(file)} vuelve a declarar un total de pasos a mano. " +
                    "El total es steps.Count; una constante paralela es la que se quedo en 53 " +
                    "mientras la secuencia crecia a 70.");
            }
        }

        // ── Order: the constraints that used to live only in comments ────────

        /// <summary>
        /// Every runtime editor registers itself with <c>GameEditorManager</c> in
        /// <c>OnEnable</c> and most do NOT retry. A manager created after them means a
        /// permanently unregistered editor: it never joins the exclusivity group, so
        /// opening it leaves whatever else was open stacked underneath. The old code
        /// was protected only by the accident that the Tile editor happened to run
        /// first and self-create the manager.
        /// </summary>
        [Test]
        public void EditorManagerAndWorkspace_AreInstalledBeforeAnyEditor()
        {
            int firstEditor = int.MaxValue;
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Label.StartsWith("Preparando el editor") ||
                    _steps[i].Label == "Inicializando el editor de tiles" ||
                    _steps[i].Label == "Inicializando el editor de mapas")
                {
                    firstEditor = i;
                    break;
                }
            }
            Assert.AreNotEqual(int.MaxValue, firstEditor, "No hay ningun editor en la secuencia.");
            Assert.GreaterOrEqual(firstEditor, 2,
                "Los dos primeros pasos deben ser GameEditorManager y EditorWorkspaceService.");
            Assert.IsFalse(_steps[0].IsReported, "El primer paso es la instalacion silenciosa del gestor.");
            Assert.IsFalse(_steps[1].IsReported, "El segundo paso es la instalacion silenciosa del workspace.");
        }

        /// <summary>
        /// TileEditorManager is the sink for every zone's collision tags. Built after
        /// the world load, the sink is null, every tag is silently dropped, and the
        /// baker stamps every cell as blocking on every visual layer.
        /// </summary>
        [Test]
        public void TileEditor_PrecedesTheWorldLoad()
            => AssertBefore("Inicializando el editor de tiles", "Cargando el mundo");

        /// <summary>
        /// Every building asks the ServiceLocator for the damage service as it spawns.
        /// Registered afterwards it is found by nothing, and every felled tree quietly
        /// comes back whole.
        /// </summary>
        [Test]
        public void WorldDamageService_PrecedesTheBuildingLoader()
            => AssertBefore("Restaurando los danos del mundo", "Levantando los edificios");

        [Test]
        public void WorldLoad_PrecedesThePlayer()
            => AssertBefore("Cargando el mundo", "Creando el personaje");

        [Test]
        public void SessionRestore_IsTheLastReportedStep()
        {
            string last = null;
            foreach (var s in _steps) if (s.IsReported) last = s.Label;
            Assert.AreEqual("Restaurando la partida", last,
                "La restauracion de la partida debe ser lo ultimo: consume el save pendiente " +
                "y fija la identidad de la telemetria contra el run id que produce.");
        }

        // ── The declared/real pair that caused the original bug ──────────────

        /// <summary>
        /// The three progressive steps narrate themselves, and each declares how many
        /// sub-stages it reports so the bar can walk its own share instead of freezing.
        /// That declaration is a second copy of something the source already knows —
        /// which is exactly the shape of the 53-against-70 defect — so it is checked
        /// against the code that actually reports.
        /// </summary>
        /// <summary>
        /// Matches only a sub-stage reported with a LITERAL label. The one call that
        /// forwards a loader's own labels (<c>stage =&gt; ReportSubStage(stage)</c>) is a
        /// single call site standing in for three stages, so counting it here would
        /// undercount the world load by two and then blame the declaration.
        /// </summary>
        private const string ExplicitSubStageCall = @"ReportSubStage\(""";

        [Test]
        public void DeclaredSubStages_MatchWhatTheSourceActuallyReports()
        {
            string scripts = Path.Combine(Application.dataPath, "_Project/Scripts");

            int world =
                CountMatches(Path.Combine(scripts, "Gameplay/Bootstrap/GameplaySceneSetup.Ensure.cs"), ExplicitSubStageCall) +
                CountMatches(Path.Combine(scripts, "Gameplay/World/Setup/WorldLoader.cs"), @"reportStage\?\.Invoke\(");

            int player = CountMatches(
                Path.Combine(scripts, "Gameplay/Bootstrap/GameplaySceneSetup.SpawnPlayer.cs"), ExplicitSubStageCall);

            int buildings = CountMatches(
                Path.Combine(scripts, "Gameplay/World/Buildings/BuildingLoader.cs"), @"reportStage\?\.Invoke\(");

            AssertSubStages("Cargando el mundo", world);
            AssertSubStages("Creando el personaje", player);
            AssertSubStages("Levantando los edificios", buildings);
        }

        private void AssertSubStages(string label, int expected)
        {
            int i = IndexOfLabel(label);
            Assert.AreNotEqual(-1, i, $"Falta la etapa '{label}'.");
            Assert.AreEqual(expected, _steps[i].SubStages,
                $"'{label}' declara {_steps[i].SubStages} sub-etapas y el codigo reporta {expected}. " +
                "Un denominador que no coincide con lo que se cuenta es el defecto original.");
        }

        private static int CountMatches(string path, string pattern)
        {
            Assert.IsTrue(File.Exists(path), $"No existe {path}");
            return Regex.Matches(File.ReadAllText(path), pattern).Count;
        }

        // ── Frame budget ─────────────────────────────────────────────────────

        /// <summary>
        /// The old runner yielded once per step unconditionally: seventy frames, 1.17 s
        /// of pure waiting at 60 Hz before any real work is counted. Collapsing the
        /// registration runs is the point of <see cref="BootStep.Barrier"/>, and this
        /// pins that the collapse actually happened — while leaving room for the steps
        /// that genuinely need a frame to have passed.
        /// </summary>
        [Test]
        public void MostStepsDoNotDemandTheirOwnFrame()
        {
            int barriers = 0;
            foreach (var s in _steps) if (s.Barrier) barriers++;
            Assert.Less(barriers, _steps.Count / 2,
                $"{barriers} de {_steps.Count} pasos piden fotograma propio. El presupuesto " +
                "de tiempo del runner deja de servir para nada.");
        }
    }
}
