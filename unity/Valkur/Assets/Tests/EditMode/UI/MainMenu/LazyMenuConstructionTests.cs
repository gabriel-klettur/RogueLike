using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.UI.MainMenu
{
    /// <summary>
    /// The menu builds the screens the player may never open only when they are asked for.
    ///
    /// <para>Everything used to be built in <c>Start</c>. Measured on the rebuilt menu, the class
    /// selector, the four option panels, the load browser and the credits are the overwhelming
    /// majority of the canvas — and uGUI charges for every one of those widgets whether or not
    /// anybody looks at them. The Items editor proved the shape of this fix at a larger scale:
    /// 3 480 ms down to 413 ms, by building fewer widgets less often.</para>
    /// </summary>
    public class LazyMenuConstructionTests
    {
        private static readonly BindingFlags PrivInst = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;
        private MainMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            _go = new GameObject("TestMainMenuUI_Lazy");
            _menu = _go.AddComponent<MainMenuUI>();
            typeof(MainMenuUI).GetMethod("Start", PrivInst).Invoke(_menu, null);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private object Field(string name)
            => typeof(MainMenuUI).GetField(name, PrivInst)?.GetValue(_menu);

        private void Show(string screen)
        {
            var enumType = typeof(MainMenuUI).GetNestedType("MenuScreen", BindingFlags.NonPublic);
            typeof(MainMenuUI).GetMethod("ShowMenuScreen", PrivInst)
                .Invoke(_menu, new[] { System.Enum.Parse(enumType, screen) });
        }

        [Test]
        public void Start_BuildsOnlyTheFirstScreen()
        {
            Assert.IsNotNull(Field("_menuPanelGo"), "the main panel is what Start is for");
            Assert.IsNull(Field("_optionsPanel"), "Options was built before anybody asked");
            Assert.IsNull(Field("_audioPanel"));
            Assert.IsNull(Field("_videoPanel"));
            Assert.IsNull(Field("_gameplayPanel"));
            Assert.IsNull(Field("_controlsPanel"));
            Assert.IsNull(Field("_creditsPanel"));
            Assert.IsNull(Field("_mmLoadOverlay"));
            Assert.IsNull(Field("_classSelectionPanel"));
        }

        /// <summary>
        /// The four option panels are built TOGETHER the moment Options is opened. One by one,
        /// stepping from Options to Audio would cost a hitch in the middle of a navigation the
        /// player is already performing.
        /// </summary>
        [Test]
        public void OpeningOptions_BuildsAllFourOfItsPanels_AndNothingElse()
        {
            Show("Options");
            Assert.IsNotNull(Field("_optionsPanel"));
            Assert.IsNotNull(Field("_audioPanel"));
            Assert.IsNotNull(Field("_videoPanel"));
            Assert.IsNotNull(Field("_gameplayPanel"));
            Assert.IsNotNull(Field("_controlsPanel"));

            Assert.IsNull(Field("_mmLoadOverlay"), "the load browser is a different journey");
            Assert.IsNull(Field("_classSelectionPanel"));
            Assert.IsNull(Field("_creditsPanel"));
        }

        [Test]
        public void OpeningLoadGame_BuildsOnlyTheLoadBrowser()
        {
            Show("LoadGame");
            Assert.IsNotNull(Field("_mmLoadOverlay"));
            Assert.IsNull(Field("_optionsPanel"));
            Assert.IsNull(Field("_classSelectionPanel"));
        }

        [Test]
        public void OpeningTheClassSelector_BuildsOnlyIt()
        {
            Show("ClassSelector");
            Assert.IsNotNull(Field("_classSelectionPanel"));
            Assert.IsNull(Field("_optionsPanel"));
            Assert.IsNull(Field("_mmLoadOverlay"));
        }

        [Test]
        public void OpeningTheCredits_BuildsOnlyThem()
        {
            Show("Credits");
            Assert.IsNotNull(Field("_creditsPanel"));
            Assert.IsNull(Field("_optionsPanel"));
        }

        [Test]
        public void AScreenIsBuiltOnce_NotOnEveryVisit()
        {
            Show("Options");
            var first = Field("_optionsPanel");
            Show("Main");
            Show("Options");
            Assert.AreSame(first, Field("_optionsPanel"), "the panel was rebuilt on the second visit");
        }

        [Test]
        public void BuildAllScreensForTests_LeavesNothingUnbuilt()
        {
            _menu.BuildAllScreensForTests();
            foreach (var name in new[] { "_optionsPanel", "_audioPanel", "_videoPanel",
                                         "_gameplayPanel", "_controlsPanel", "_creditsPanel",
                                         "_mmLoadOverlay", "_classSelectionPanel" })
                Assert.IsNotNull(Field(name), name + " was not built");
        }

        /// <summary>
        /// The canvas a player who only ever presses "New game" pays for. A guard against the
        /// lazy path quietly being undone by somebody adding a builder back into BuildUI.
        /// </summary>
        [Test]
        public void TheFirstScreen_IsASmallCanvas()
        {
            var canvas = _menu.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas);
            int graphics = canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length;
            Assert.Less(graphics, 90,
                $"the title screen alone built {graphics} graphics; the screens behind it are " +
                "supposed to wait until they are opened");
        }
    }
}
