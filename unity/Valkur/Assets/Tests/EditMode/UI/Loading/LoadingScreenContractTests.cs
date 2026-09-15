using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;
using Valkur.UI.Loading;

namespace Valkur.Tests.EditMode.UI.Loading
{
    /// <summary>
    /// The loading screen's contract with the rest of the game: one door into a scene
    /// load, a watchdog that measures progress rather than the clock, and player-facing
    /// text that names controls which exist.
    ///
    /// <para>Most of these are source scans, and deliberately so. uGUI performs no
    /// layout in EditMode and a component added there never receives <c>Awake</c> or
    /// <c>OnEnable</c>, so a fixture that "exercises" the screen measures the values it
    /// wrote itself — this repository has already paid for that lesson twice. What CAN
    /// be asserted honestly is the shape of the code and the content of the data, and
    /// every check below is one of those.</para>
    /// </summary>
    [TestFixture]
    public class LoadingScreenContractTests
    {
        private static string Scripts => Path.Combine(Application.dataPath, "_Project/Scripts");

        private static string Read(string relative)
        {
            string path = Path.Combine(Scripts, relative);
            Assert.IsTrue(File.Exists(path), $"No existe {relative}");
            return File.ReadAllText(path);
        }

        // ── One door ─────────────────────────────────────────────────────────

        /// <summary>
        /// Four of the game's six scene transitions used to call
        /// <c>SceneManager.LoadScene</c> directly — pause &gt; New Game, pause &gt;
        /// Exit, a ZonePortal with a destination scene, and the first Bootstrap &gt;
        /// MainMenu hop — so they re-ran the whole gameplay boot behind a frozen frame
        /// with nothing on screen. <c>SceneTransitionManager</c> is the only place
        /// allowed to reach the engine directly, and the loading screen is the only
        /// place allowed to start the async load.
        /// </summary>
        [Test]
        public void OnlyTheTransitionManagerLoadsAScene_AndOnlyTheScreenLoadsItAsync()
        {
            foreach (var file in Directory.GetFiles(Scripts, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                string src = File.ReadAllText(file);

                if (Regex.IsMatch(src, @"SceneManager\.LoadScene\s*\("))
                    Assert.AreEqual("SceneTransitionManager.cs", name,
                        $"{name} carga una escena por su cuenta. Toda transicion pasa por " +
                        "SceneTransitionManager, que es lo que le da pantalla de carga.");

                if (Regex.IsMatch(src, @"SceneManager\.LoadSceneAsync\s*\("))
                    Assert.AreEqual("LoadingScreenController.cs", name,
                        $"{name} arranca una carga asincrona fuera de la pantalla de carga.");
            }
        }

        [Test]
        public void TheRouterIsInstalledBeforeTheFirstSceneLoads()
        {
            string src = Read("UI/Loading/SceneLoadRouter.cs");
            Assert.IsTrue(src.Contains("RuntimeInitializeLoadType.BeforeSceneLoad"),
                "El router debe instalarse antes de la primera escena: la primera transicion " +
                "del juego es GameBootstrap.Start, y un hook posterior se la pierde.");
            Assert.IsTrue(src.Contains("SceneTransitionManager.LoadSceneHandler"),
                "El router ya no instala el manejador.");
        }

        /// <summary>
        /// One door is not one curtain, and conflating the two shipped a regression:
        /// with every transition SHOWING the screen, the player got a full loading
        /// screen in front of the main menu — which loads in a blink and reports
        /// nothing — and then a second one the instant they pressed New Game. Two
        /// screens back to back at startup.
        ///
        /// Routing everything through <c>SceneTransitionManager</c> is still right; it
        /// is what gives pause &gt; New Game its seventy stages of feedback. What the
        /// screen is FOR is a scene with a boot sequence behind it.
        /// </summary>
        [Test]
        public void OnlyASceneWithABootSequenceGetsAScreen()
        {
            Assert.IsTrue(SceneLoadRouter.NeedsLoadingScreen("MainGameplay"),
                "La escena de juego corre 64 etapas detras: es justo para lo que existe la pantalla.");
            Assert.IsFalse(SceneLoadRouter.NeedsLoadingScreen("MainMenu"),
                "El menu carga en un parpadeo. Una pantalla de carga delante son dos " +
                "pantallas seguidas al abrir el juego y la primera no reporta nada.");
            Assert.IsFalse(SceneLoadRouter.NeedsLoadingScreen("Bootstrap"),
                "La escena Bootstrap no tiene nada que narrar.");
            Assert.IsFalse(SceneLoadRouter.NeedsLoadingScreen(null));
            Assert.IsFalse(SceneLoadRouter.NeedsLoadingScreen("   "));
        }

        [Test]
        public void TheGameplaySceneIsNotHardCodedTwice()
        {
            string src = Read("UI/Loading/SceneLoadRouter.cs");
            Assert.IsTrue(src.Contains("GameBootstrap.GameplaySceneName"),
                "El nombre de la escena de juego debe venir del campo que publica " +
                "GameBootstrap, no de una segunda copia que se queda atras cuando alguien " +
                "renombra la escena en el inspector.");
        }

        [Test]
        public void TheTransitionManagerFallsBackWhenTheScreenRefuses()
        {
            string src = Read("Core/SceneTransitionManager.cs");
            Assert.IsTrue(src.Contains("LoadSceneImmediate"),
                "Sin camino de respaldo, un relevo roto deja al jugador atrapado en la escena actual.");
            Assert.IsTrue(Regex.IsMatch(src, @"catch\s*\("),
                "El manejador se invoca sin proteccion: una excepcion en la UI cancelaria la transicion.");
        }

        /// <summary>
        /// Declining is a NORMAL answer, so it must not be modelled as an exception.
        /// The delegate is static and Domain Reload is off, so the handler a Play session
        /// installs is still installed in the Editor afterwards — and an EditMode fixture
        /// that exercised the pause menu's Exit went red on the error log of a fallback
        /// that had worked perfectly.
        /// </summary>
        [Test]
        public void DecliningALoadIsAnAnswer_NotAnError()
        {
            string manager = Read("Core/SceneTransitionManager.cs");
            Assert.IsTrue(manager.Contains("Func<string, bool> LoadSceneHandler"),
                "El relevo vuelve a ser un Action: declinar solo se puede expresar lanzando.");

            string router = Read("UI/Loading/SceneLoadRouter.cs");
            Assert.IsTrue(router.Contains("Application.isPlaying"),
                "El router no comprueba el Play Mode, asi que en el Editor intenta montar " +
                "una pantalla con DontDestroyOnLoad, que Unity rechaza.");
            Assert.IsFalse(Regex.IsMatch(router, @"\bthrow\b"),
                "El router vuelve a lanzar para declinar.");
        }

        // ── The watchdog ─────────────────────────────────────────────────────

        /// <summary>
        /// The shipped watchdog was a flat fifteen seconds from scene activation —
        /// clock time, not silence — so on a slower machine the screen faded out WHILE
        /// the boot was still running. A heartbeat asks the only question worth asking:
        /// not "is this slow" but "is this dead".
        /// </summary>
        [Test]
        public void TheWatchdogMeasuresSilence_NotElapsedTime()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");

            Assert.IsFalse(src.Contains("FadeWatchdog"),
                "Ha vuelto el watchdog de plazo absoluto.");
            Assert.IsTrue(src.Contains("_lastProgressTime = Time.unscaledTime"),
                "El watchdog ya no se rearma con el progreso; vuelve a ser un plazo.");
            Assert.IsTrue(Regex.IsMatch(src, @"StallWarnSeconds"),
                "Falta el aviso previo: pasar de 'todo bien' a 'ha fallado' sin avisar es peor.");
            Assert.IsTrue(Regex.IsMatch(src, @"now\s*-\s*_lastProgressTime"),
                "El watchdog no compara contra el ultimo avance.");
        }

        [Test]
        public void AStalledBootEndsOnAScreen_NotInABrokenWorld()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            Assert.IsTrue(src.Contains("ShowErrorPanel"),
                "No hay superficie de error: un arranque muerto volvia a desvanecerse en silencio.");
            Assert.IsTrue(src.Contains("BootTimeline.AnyFailed"),
                "La pantalla no consulta si algun paso fallo, asi que un arranque a medias " +
                "termina como uno limpio.");
            Assert.IsTrue(src.Contains("Volver al menu"),
                "La pantalla de error no ofrece salida.");
        }

        [Test]
        public void TheAsyncLoadIsNullChecked_AndTheSceneValidated()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            Assert.IsTrue(src.Contains("CanStreamedLevelBeLoaded"),
                "Una escena ausente de Build Settings devuelve null y reventaba en la linea " +
                "siguiente, con la pantalla ya montada y sin salida.");
            Assert.IsTrue(Regex.IsMatch(src, @"asyncOp\s*==\s*null"),
                "Falta la comprobacion de nulo sobre LoadSceneAsync.");
        }

        [Test]
        public void ShowIsIdempotent_AndSaysSo()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            Assert.IsTrue(Regex.IsMatch(src, @"public static bool Show"),
                "Show debe informar de si acepto la carga; el router necesita esa respuesta.");
            Assert.IsTrue(Regex.IsMatch(src, @"if\s*\(_instance\s*!=\s*null\)"),
                "Show vuelve a poder crear dos pantallas y dos LoadSceneAsync simultaneos. " +
                "El campo _instance existia justo para esto y no lo leia nadie.");
        }

        /// <summary>
        /// The block needs its OWN owner, and that was found live rather than reasoned
        /// about. The chat system is CREATED during the boot, and its gate's first
        /// <c>Refresh()</c> answers "no panel is open" by writing <c>false</c> — which,
        /// on a single shared bool, cleared the loading screen's block with the world
        /// still 40 % assembled. Same shape as <c>Health.SetInvincible</c>, which this
        /// project has already shipped broken twice.
        /// </summary>
        [Test]
        public void GameplayInputIsBlockedWhileTheWorldAssemblesItself()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            Assert.IsTrue(src.Contains("InputBlocker.SetBootBlocked"),
                "Desde que se activa la escena el jugador podia moverse y disparar " +
                "mientras las ultimas etapas registraban sus sistemas.");
        }

        [Test]
        public void TheBootBlockCannotBeClearedByThePanelOwner()
        {
            Valkur.Core.Input.InputBlocker.SetBlocked(false);
            Valkur.Core.Input.InputBlocker.SetBootBlocked(false);
            try
            {
                Valkur.Core.Input.InputBlocker.SetBootBlocked(true);
                Assert.IsTrue(Valkur.Core.Input.InputBlocker.IsGameplayBlocked);

                // What ChatInputGate.Refresh() does on the frame the chat system is built.
                Valkur.Core.Input.InputBlocker.SetBlocked(false);
                Assert.IsTrue(Valkur.Core.Input.InputBlocker.IsGameplayBlocked,
                    "El dueño de los paneles ha borrado el bloqueo del arranque. Es " +
                    "exactamente lo que se midio en vivo con el mundo al 40 %.");

                Valkur.Core.Input.InputBlocker.SetBootBlocked(false);
                Assert.IsFalse(Valkur.Core.Input.InputBlocker.IsGameplayBlocked,
                    "Al terminar el arranque la entrada debe soltarse.");
            }
            finally
            {
                Valkur.Core.Input.InputBlocker.SetBlocked(false);
                Valkur.Core.Input.InputBlocker.SetBootBlocked(false);
            }
        }

        // ── The "no phase 2" decision ────────────────────────────────────────

        /// <summary>
        /// Found live, not reasoned about: with a seconds-only grace window the screen
        /// concluded the gameplay scene had nothing to say and faded out with the boot
        /// at 72.8 %. Activating that scene is ONE enormous frame — Unity tears down the
        /// menu and awakes everything in MainGameplay — so a whole second of wall clock
        /// can expire before <c>GameplaySceneSetup.Start</c> runs its first line.
        ///
        /// A frame count cannot be fooled that way, which is why both gates exist and
        /// why the slower of the two wins.
        /// </summary>
        [Test]
        public void OneHugeActivationFrame_IsNotMistakenForASilentScene()
        {
            Assert.IsFalse(LoadingScreenController.ShouldConcludeNoPhase2(1, 5f),
                "Un solo fotograma de activacion, por largo que sea, no significa que la " +
                "escena no tenga nada que decir. Este es el fallo que se midio en vivo.");
            Assert.IsFalse(LoadingScreenController.ShouldConcludeNoPhase2(2, 30f),
                "Dos fotogramas siguen sin ser prueba de silencio.");
        }

        [Test]
        public void AFastMachineStillWaitsTheFullWindow()
        {
            Assert.IsFalse(LoadingScreenController.ShouldConcludeNoPhase2(60, 0.1f),
                "Sesenta fotogramas en una decima de segundo no bastan: el arranque puede " +
                "estar todavia a punto de hablar.");
        }

        [Test]
        public void AGenuinelySilentSceneIsReleased()
        {
            Assert.IsTrue(LoadingScreenController.ShouldConcludeNoPhase2(30, 2f),
                "Una escena ligera (el menu) debe soltarse; si no, cada transicion cuesta " +
                "el minimo pensado para el arranque del juego.");
        }

        [Test]
        public void NothingIsConcludedAtActivation()
            => Assert.IsFalse(LoadingScreenController.ShouldConcludeNoPhase2(0, 0f));

        /// <summary>
        /// The threshold was never the real fix and this pins the thing that was: the
        /// window starts when the activation FINISHES, not when it is permitted. Unity
        /// runs the incoming scene's Start during activation, so once
        /// <c>yield return asyncOp</c> resumes, a scene with a boot sequence has already
        /// reported. Waiting on the operation is what makes the numbers above a cushion
        /// instead of a bet — the first two attempts tuned the threshold and both still
        /// dropped the player into a half-built world.
        /// </summary>
        [Test]
        public void TheGraceWindowStartsWhenActivationFinishes()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            int wait = src.IndexOf("yield return asyncOp;");
            int stamp = src.IndexOf("_activationFrame = Time.frameCount;");
            Assert.AreNotEqual(-1, wait,
                "La pantalla ya no espera a que la activacion TERMINE; vuelve a medir la " +
                "propia activacion como silencio.");
            Assert.AreNotEqual(-1, stamp, "Falta el sello del fotograma de activacion.");
            Assert.Less(wait, stamp,
                "La ventana de gracia se sella antes de que la activacion acabe, que es " +
                "exactamente el fallo medido: fundido con el arranque al 15 %.");
        }

        // ── Player-facing text ───────────────────────────────────────────────

        /// <summary>
        /// Five of the ten shipped tips named keys retired on 2026-09-05. The fourteen
        /// editor toggles ship UNBOUND and the way in is Escape; F4 was never the
        /// spells editor. A loading screen that teaches controls which do not exist is
        /// worse than one with no tips at all.
        /// </summary>
        [Test]
        public void NoTipNamesAnFKey()
        {
            foreach (var tip in LoadingText.Tips)
                Assert.IsFalse(Regex.IsMatch(tip, @"\bF(1[0-2]|[1-9])\b"),
                    $"Este consejo nombra una tecla de funcion retirada: \"{tip}\"");
        }

        [Test]
        public void TipsAreNonEmptyAndDistinct()
        {
            Assert.Greater(LoadingText.Tips.Length, 4, "Con tan pocos consejos se repiten en una sola carga.");
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var tip in LoadingText.Tips)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(tip), "Hay un consejo vacio.");
                Assert.IsTrue(seen.Add(tip), $"Consejo duplicado: \"{tip}\"");
            }
        }

        /// <summary>
        /// The file this text replaced shipped with its own comment banners rendered as
        /// mojibake, so the loading screen is the one surface in the project with a
        /// demonstrated encoding hazard. Plain ASCII cannot be corrupted by a tool that
        /// rewrites the file in the wrong codepage.
        /// </summary>
        [Test]
        public void PlayerFacingTextIsPlainAscii()
        {
            foreach (var tip in LoadingText.Tips)
                foreach (char c in tip)
                    Assert.Less((int)c, 128,
                        $"Caracter no ASCII '{c}' en \"{tip}\". Este fichero ya se corrompio una vez.");
        }

        [Test]
        public void TheScreenTakesItsStringsFromOnePlace()
        {
            string src = Read("UI/Loading/LoadingScreenController.cs");
            Assert.IsTrue(src.Contains("LoadingText."),
                "Las cadenas volvieron al controlador.");
            Assert.IsFalse(src.Contains("\"Loading\""),
                "El texto en ingles ha vuelto a la pantalla de carga.");
            Assert.IsFalse(Regex.IsMatch(src, @"static readonly string\[\]\s+LoadingTips"),
                "La lista de consejos vuelve a estar enterrada en el MonoBehaviour.");
        }
    }
}
