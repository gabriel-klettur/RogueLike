using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Gameplay.Chat
{
    /// <summary>
    /// Exercises <c>ChatUI.BuildUI</c> (ChatUI.Builder.cs) and the private references that the
    /// rest of <see cref="ChatUI"/> (ChatUI.cs) dereferences without any null guard.
    ///
    /// Why this matters: BuildUI runs exactly once, from Start(), and every later code path
    /// (OnChatOpened, OnChatClosed, OnMessageReceived, SubmitInput, ToggleLang, Update) assumes
    /// the hierarchy it produced is complete and correctly wired. A silent regression in the
    /// builder - a missing child, a ScrollRect whose viewport/content was never assigned, an
    /// Image and a TextMeshProUGUI landing on the same GameObject - does not surface until the
    /// chat panel is opened in play mode, where it shows up as a NullReferenceException or as
    /// an invisible / unscrollable panel.
    ///
    /// Start() does not run in EditMode, so BuildUI is invoked directly through reflection.
    /// The whole hierarchy hangs off the ChatUI GameObject, which TearDown destroys.
    /// </summary>
    [TestFixture]
    public partial class ChatUIBuilderTests
    {
        private GameObject _hostGo;
        private ChatUI _ui;

        /// <summary>
        /// Keys the panel remembers its size under. Cleared around every test, because
        /// PlayerPrefs is MACHINE state, not fixture state: it survives the run, the Editor
        /// and the reboot. Without this, resizing the chat window once by hand — in the
        /// Editor, in a build, on any machine that ever ran this suite — would leave the
        /// default-size assertion below failing forever, and it would fail for a reason
        /// nothing in the test names.
        /// </summary>
        private static readonly string[] PanelSizePrefKeys =
        {
            "valkur.chat.panel.width",
            "valkur.chat.panel.height",
            LayoutVersionPrefKey,
        };

        /// <summary>
        /// Which arrangement the remembered size was measured on. Cleared with the size for
        /// the same reason, and STAMPED by the two restore tests below: a size saved against
        /// an older layout is deliberately discarded, so a test that sets a size without
        /// saying which layout it belongs to would be exercising the discard path while
        /// claiming to exercise the restore one.
        /// </summary>
        private const string LayoutVersionPrefKey = "valkur.chat.panel.layout";

        /// <summary>Must track <c>ChatUI.PANEL_LAYOUT_VERSION</c>.</summary>
        private const int CurrentLayoutVersion = 4;

        private const string LANGUAGE_PREF_KEY = "valkur.chat.language";

        private static void ClearPanelSizePrefs()
        {
            for (int i = 0; i < PanelSizePrefKeys.Length; i++)
                PlayerPrefs.DeleteKey(PanelSizePrefKeys[i]);
        }

        private string _savedLanguage;

        [SetUp]
        public void SetUp()
        {
            // Building UGUI/TMP objects in EditMode emits assorted initialisation noise.
            LogAssert.ignoreFailingMessages = true;

            ClearPanelSizePrefs();

            // The panel's captions are a function of a GLOBAL, PERSISTED preference now, so
            // asserting "Enviar" and "ES" is asserting machine state unless the fixture pins
            // it. It bit exactly that way: these three tests went red because the language
            // had been left on English by something else entirely, and they would then have
            // stayed red on this machine only, for a reason nothing in their names mentions.
            // Same rule the panel-size prefs above are already cleared for.
            _savedLanguage = PlayerPrefs.GetString(LANGUAGE_PREF_KEY, ChatLanguage.SPANISH);
            ChatLanguage.Set(ChatLanguage.SPANISH);

            // Defensive: a leaked ChatUI from another fixture would make the singleton
            // duplicate-guard call Destroy() (illegal in EditMode) from inside our Awake.
            if (ChatUI.HasInstance && ChatUI.Instance != null)
                UnityEngine.Object.DestroyImmediate(ChatUI.Instance.gameObject);
            ClearSingleton<ChatUI>();

            // The three button-callback tests below require ChatSystem.Instance to be
            // absent. HasInstance is a Unity fake-null check, so a *destroyed* leaked
            // instance reports false while ChatSystem.Instance?.CloseChat() still
            // dereferences it and throws MissingReferenceException. Null the static
            // outright so the precondition is real rather than probabilistic.
            ClearSingleton<ChatSystem>();

            _hostGo = new GameObject("ChatUIHost");
            _ui = _hostGo.AddComponent<ChatUI>();

            BuildUI();
        }

        [TearDown]
        public void TearDown()
        {
            ChatLanguage.Set(_savedLanguage);

            if (_hostGo != null) UnityEngine.Object.DestroyImmediate(_hostGo);
            _hostGo = null;
            _ui = null;
            // Don't leak our ChatUI into the next fixture: OnDestroy only nulls the
            // static when Unity actually delivers the message, which EditMode does
            // not guarantee for components added via AddComponent.
            ClearSingleton<ChatUI>();

            // Leaving a size behind would hand it to the NEXT fixture that builds a panel.
            ClearPanelSizePrefs();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// One of ChatUI's private layout constants, read rather than duplicated: a literal
        /// copied into a test passes forever after the production value moves.
        /// </summary>
        private static float Const(string name)
        {
            var fi = typeof(ChatUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(fi, $"ChatUI.{name} was renamed or removed.");
            return (float)fi.GetValue(null);
        }

        /// <summary>Nulls SingletonMonoBehaviour&lt;T&gt;'s private static _instance slot.</summary>
        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var fi = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (fi != null) { fi.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        // -------------------------------------------------------------------------
        // Reflection helpers - BuildUI and every reference it fills are private.
        // -------------------------------------------------------------------------

        private void BuildUI()
        {
            var mi = typeof(ChatUI).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.BuildUI() was renamed or removed - the builder contract changed.");
            Invoke(mi, _ui, null);
        }

        /// <summary>
        /// Throws away the panel SetUp built and builds a fresh one, for the tests that need
        /// the builder to read state written after SetUp ran.
        ///
        /// A second <c>BuildUI()</c> would do nothing — it opens with <c>if (_isBuilt) return;</c>
        /// — so the host has to go with it. Without this the remembered-size tests would set a
        /// preference, assert against the panel built before it existed, and pass or fail on
        /// the default every time.
        /// </summary>
        private void Rebuild()
        {
            if (_hostGo != null) UnityEngine.Object.DestroyImmediate(_hostGo);
            ClearSingleton<ChatUI>();

            _hostGo = new GameObject("ChatUIHost");
            _ui = _hostGo.AddComponent<ChatUI>();
            BuildUI();
        }

        private void AppendMessageRow(string sender, string text)
        {
            var mi = typeof(ChatUI).GetMethod("AppendMessageRow", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.AppendMessageRow(string,string) was renamed or removed.");
            Invoke(mi, _ui, new object[] { sender, text });
        }

        /// <summary>Invokes and unwraps TargetInvocationException so failures report the real error.</summary>
        private static void Invoke(MethodBase method, object target, object[] args)
        {
            try
            {
                method.Invoke(target, args);
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException ?? tie;
            }
        }

        private T Field<T>(string name)
        {
            var fi = typeof(ChatUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "ChatUI field '" + name + "' is missing - ChatUI.cs and ChatUI.Builder.cs are out of sync.");
            return (T)fi.GetValue(_ui);
        }

        private GameObject CanvasGo => Field<Canvas>("_canvas").gameObject;
        private GameObject Panel => Field<GameObject>("_panel");
        private GameObject Backdrop => Field<GameObject>("_backdrop");

        private static GameObject Child(GameObject parent, string path)
        {
            var t = parent.transform.Find(path);
            Assert.IsTrue(t != null, "Expected child '" + path + "' under '" + parent.name + "' - hierarchy changed.");
            return t.gameObject;
        }

    }
}
