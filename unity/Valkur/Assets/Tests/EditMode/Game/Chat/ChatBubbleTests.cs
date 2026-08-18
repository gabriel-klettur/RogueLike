using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Covers <see cref="ChatBubble"/> — the world-space floating bubble that
    /// <c>ChatSystem</c> attaches to an NPC (and to the player) to show dialogue
    /// lines above their heads.
    ///
    /// Why it matters: the bubble is built entirely from code at runtime
    /// (Canvas + Image + TMP hierarchy, stacking offsets, TTL bookkeeping), so
    /// there is no prefab or inspector wiring that would catch a regression.
    /// A silent break here means chat lines render on the wrong sorting layer,
    /// overlap each other, get clipped, or stop following the speaker.
    ///
    /// EditMode strategy:
    ///  * <c>LateUpdate</c> is a private Unity message and is never pumped in
    ///    EditMode, so it is invoked through reflection. That is the only way to
    ///    assert follow/billboard behaviour without entering Play mode.
    ///  * <c>Time.time</c> does not advance inside an EditMode test, so the
    ///    fade/expire branches cannot be driven forward. What IS asserted is the
    ///    inverse contract: a bubble pushed with a normal TTL must survive
    ///    repeated LateUpdate ticks (a regression that expires bubbles instantly
    ///    would call <c>Object.Destroy</c>, illegal in edit mode, and surface as
    ///    a thrown exception through the reflection call).
    ///  * The bubble host is deliberately NOT parented to the follow target
    ///    (production parents it) so that destroying the target exercises
    ///    ChatBubble's own null guard instead of taking the bubble down with it.
    /// </summary>
    [TestFixture]
    public class ChatBubbleTests
    {
        // Defaults baked into ChatBubble's [SerializeField] initialisers.
        private const float DefaultYOffset = 1.5f;
        private const float DefaultMaxWidth = 3f;
        private const float CanvasScale = 0.02f;

        private const string LongMessage =
            "El comerciante te mira de reojo y comienza un discurso interminable sobre " +
            "el precio de la sal, los bandidos del camino del norte y la cosecha perdida " +
            "del anyo pasado, sin detenerse ni una sola vez a tomar aire.";

        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Building UI (Canvas / Image / TMP) in EditMode emits incidental
            // renderer + font-atlas messages that would otherwise fail the test.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy the bubble children BEFORE their host: ChatBubble.OnDestroy
            // calls Object.Destroy on every live entry, and Destroy() is illegal
            // in edit mode. Killing the children first makes the `b.go != null`
            // guard skip that illegal call.
            foreach (var go in _created)
            {
                if (go == null) continue;
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            }

            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);

            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        /// <summary>Creates a bubble host and initialises it against <paramref name="target"/>.</summary>
        private ChatBubble CreateBubble(Transform target)
        {
            var bubble = NewGo("ChatBubbleHost").AddComponent<ChatBubble>();
            bubble.Initialize(target);
            return bubble;
        }

        /// <summary>
        /// Invokes the private LateUpdate message, unwrapping the reflection
        /// wrapper so failures report the real exception.
        /// </summary>
        private static void Tick(ChatBubble bubble)
        {
            var method = typeof(ChatBubble).GetMethod(
                "LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method,
                "ChatBubble.LateUpdate must exist — the follow, billboard and expire " +
                "logic all live in it; renaming it silently disables the bubble.");

            try
            {
                method.Invoke(bubble, null);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static RectTransform BubbleRectAt(ChatBubble bubble, int index)
        {
            Assert.Greater(bubble.transform.childCount, index,
                "Expected at least " + (index + 1) + " bubble child object(s) under the host.");
            return (RectTransform)bubble.transform.GetChild(index);
        }

        private static TextMeshProUGUI TextOf(RectTransform bubbleRect)
        {
            var tmp = bubbleRect.GetComponentInChildren<TextMeshProUGUI>(true);
            Assert.IsTrue(tmp != null,
                "Each bubble must carry a TextMeshProUGUI on a CHILD object — TMP and Image " +
                "on the same GameObject is the documented EditMode NRE trap.");
            return tmp;
        }

        // ── Initialize ───────────────────────────────────────────────────────

        [Test]
        public void Initialize_WithTarget_ConfiguresWorldSpaceCanvasOnOverheadLayer()
        {
            var target = NewGo("Speaker");

            var bubble = CreateBubble(target.transform);

            var canvas = bubble.GetComponent<Canvas>();
            Assert.IsTrue(canvas != null,
                "Initialize must add a Canvas — without it the bubble draws nothing and the " +
                "child TMP has no Canvas ancestor to register with.");
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode,
                "The bubble lives in the world above an entity; a ScreenSpace canvas would " +
                "pin it to a screen corner instead of following the speaker.");
            Assert.AreEqual("Overhead", canvas.sortingLayerName,
                "Bubbles must render on the 'Overhead' sorting layer so walls, VFX and props " +
                "never cover dialogue text.");
            Assert.AreEqual(100, canvas.sortingOrder,
                "sortingOrder 100 keeps the bubble above every other Overhead element.");
        }

        [Test]
        public void Initialize_WithTarget_SizesCanvasRectToMaxWidthAndWorldScale()
        {
            var target = NewGo("Speaker");

            var bubble = CreateBubble(target.transform);

            var rect = bubble.GetComponent<RectTransform>();
            Assert.IsTrue(rect != null,
                "Adding a Canvas must yield a RectTransform — Initialize dereferences it " +
                "immediately, so losing it is an instant NullReferenceException.");
            Assert.AreEqual(DefaultMaxWidth, rect.sizeDelta.x, 0.0001f,
                "The canvas width must equal _maxWidth (world units) — it is the wrap budget.");
            Assert.AreEqual(CanvasScale, rect.localScale.x, 0.0001f,
                "localScale must stay 0.02: canvas units are converted to world units by it, " +
                "and drift here makes every bubble comically large or invisible.");
            Assert.AreEqual(CanvasScale, rect.localScale.y, 0.0001f,
                "Scale must stay uniform, otherwise the bubble renders stretched.");
        }

        // ── PushBubble: content ──────────────────────────────────────────────

        [Test]
        public void PushBubble_WithMessage_CreatesBubbleWithTextChildCarryingExactMessage()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble("Buenos dias, viajero.");

            Assert.AreEqual(1, bubble.transform.childCount,
                "Exactly one bubble object must be spawned per PushBubble call.");
            var rect = BubbleRectAt(bubble, 0);
            Assert.IsTrue(rect.GetComponent<Image>() != null,
                "The bubble root must own the background Image.");
            Assert.IsTrue(rect.GetComponent<CanvasGroup>() != null,
                "A CanvasGroup is required — the TTL fade drives its alpha.");
            Assert.AreEqual("Buenos dias, viajero.", TextOf(rect).text,
                "The message must reach TMP verbatim, with no trimming or re-encoding.");
        }

        [Test]
        public void PushBubble_WithoutColors_AppliesWhiteTextAndDarkTranslucentBackground()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble("Sin colores");

            var rect = BubbleRectAt(bubble, 0);
            var bg = rect.GetComponent<Image>().color;
            Assert.AreEqual(0.08f, bg.r, 0.001f, "Default background must stay near-black.");
            Assert.AreEqual(0.85f, bg.a, 0.001f,
                "Default background alpha 0.85 keeps the world faintly visible behind the " +
                "bubble; a fully opaque default would hide the speaker.");
            Assert.AreEqual(Color.white, TextOf(rect).color,
                "Default text colour must be white — the readable contrast against the dark " +
                "background.");
        }

        [Test]
        public void PushBubble_WithExplicitColors_AppliesRequestedColors()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);
            var textColor = new Color(1f, 0.25f, 0.5f, 1f);
            var backColor = new Color(0f, 0.4f, 0.2f, 0.6f);

            bubble.PushBubble("Con colores", 2500, textColor, backColor);

            var rect = BubbleRectAt(bubble, 0);
            Assert.AreEqual(textColor, TextOf(rect).color,
                "Caller-supplied text colour must win over the white default — speaker " +
                "identity (player vs NPC) is conveyed by colour.");
            Assert.AreEqual(backColor, rect.GetComponent<Image>().color,
                "Caller-supplied background colour must win over the dark default.");
        }

        [Test]
        public void PushBubble_UnicodeMessage_PreservesTextVerbatim()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);
            const string unicode = "Canyon nandu — 日本語 «¡Hola!»";

            bubble.PushBubble(unicode);

            Assert.AreEqual(unicode, TextOf(BubbleRectAt(bubble, 0)).text,
                "Accented, CJK and guillemet characters must survive untouched — the chat " +
                "backend is Spanish-first and any sanitising would mangle real dialogue.");
        }

        [Test]
        public void PushBubble_EmptyMessage_StillCreatesBubbleWithFinitePositiveSize()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble(string.Empty);

            var rect = BubbleRectAt(bubble, 0);
            Assert.AreEqual(string.Empty, TextOf(rect).text,
                "An empty chunk must not be substituted with placeholder text.");
            Assert.IsFalse(float.IsNaN(rect.sizeDelta.x) || float.IsNaN(rect.sizeDelta.y),
                "TMP preferred-size maths must never produce NaN — a NaN RectTransform size " +
                "corrupts the whole canvas layout and is very hard to trace back to here.");
            Assert.Greater(rect.sizeDelta.x, 0f,
                "Even an empty message keeps the horizontal padding, so width stays positive.");
            Assert.Greater(rect.sizeDelta.y, 0f,
                "Even an empty message keeps the vertical padding, so height stays positive.");
        }

        // ── PushBubble: wrapping / truncation ────────────────────────────────

        [Test]
        public void PushBubble_Always_EnablesWordWrapAndOverflowInsteadOfTruncating()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble(LongMessage);

            var tmp = TextOf(BubbleRectAt(bubble, 0));
            Assert.IsTrue(tmp.enableWordWrapping,
                "Word wrapping must stay on — with it off a long line runs off screen " +
                "instead of stacking into multiple rows.");
            Assert.AreEqual(TextOverflowModes.Overflow, tmp.overflowMode,
                "Overflow (not Truncate/Ellipsis) is the contract: dialogue is never cut, the " +
                "box grows instead. Switching this silently swallows message tails.");
        }

        [Test]
        public void PushBubble_LongMessage_WrapsIntoMultipleLinesUsingTheMaxWidthBudget()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble("Hi");
            bubble.PushBubble(LongMessage);

            var shortRect = BubbleRectAt(bubble, 0);
            var longRect = BubbleRectAt(bubble, 1);

            // The height is the observable proof that the wrap budget reached TMP:
            // GetPreferredValues is called with (_maxWidth / 0.02) as the line width,
            // so ~220 characters must fold into several lines. Passing 0 / no width
            // there would leave the text on a single line and the two heights would
            // be identical.
            Assert.Greater(longRect.sizeDelta.y, shortRect.sizeDelta.y * 2f,
                "A ~220-character line must wrap into several rows, making the box at least " +
                "twice as tall as a one-word bubble. Equal-ish heights mean the _maxWidth " +
                "wrap budget stopped reaching TMP and the text renders as one endless line.");
            Assert.IsFalse(float.IsNaN(longRect.sizeDelta.x) || float.IsInfinity(longRect.sizeDelta.x),
                "A wrapped bubble must still resolve to a finite width — NaN/Infinity here " +
                "silently corrupts the canvas layout for every other bubble in the stack.");
            Assert.Greater(longRect.sizeDelta.x, shortRect.sizeDelta.x,
                "The box must still be sized from the measured text, not from a constant.");
        }

        // ── PushBubble: stacking ─────────────────────────────────────────────

        [Test]
        public void PushBubble_SecondMessage_StacksAboveFirstInsteadOfReplacingIt()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble("Primera linea");
            var first = BubbleRectAt(bubble, 0);
            float firstHeight = first.sizeDelta.y;

            bubble.PushBubble("Segunda linea");

            Assert.AreEqual(2, bubble.transform.childCount,
                "A second message for the same speaker must ADD a bubble, not replace the " +
                "first one — chat chunks are meant to be readable together.");
            Assert.AreEqual(0f, first.anchoredPosition.y, 0.0001f,
                "The existing bubble must not be moved when a newer one arrives.");
            Assert.AreEqual(firstHeight + 5f, BubbleRectAt(bubble, 1).anchoredPosition.y, 0.0001f,
                "The new bubble sits directly above the previous one (its height + 5 units of " +
                "gap); a wrong offset makes bubbles overlap and become unreadable.");
        }

        [Test]
        public void PushBubble_ThirdMessage_OffsetsByCumulativeHeightOfAllPrevious()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);

            bubble.PushBubble("Uno");
            bubble.PushBubble(LongMessage);   // deliberately a tall, wrapped bubble
            float stacked = BubbleRectAt(bubble, 0).sizeDelta.y + 5f
                          + BubbleRectAt(bubble, 1).sizeDelta.y + 5f;

            bubble.PushBubble("Tres");

            Assert.AreEqual(3, bubble.transform.childCount,
                "Every pushed message keeps its own object until its TTL expires.");
            Assert.AreEqual(stacked, BubbleRectAt(bubble, 2).anchoredPosition.y, 0.0001f,
                "The offset must accumulate the real height of EVERY live bubble, not a fixed " +
                "per-bubble step — otherwise a tall wrapped bubble gets overlapped.");
        }

        // ── LateUpdate: follow / billboard ───────────────────────────────────

        [Test]
        public void LateUpdate_WithLiveTarget_PositionsBubbleAboveTargetByYOffset()
        {
            var target = NewGo("Speaker");
            target.transform.position = new Vector3(4f, -2f, 0f);
            var bubble = CreateBubble(target.transform);

            Tick(bubble);

            Vector3 expected = target.transform.position + Vector3.up * DefaultYOffset;
            Assert.AreEqual(expected.x, bubble.transform.position.x, 0.0001f,
                "The bubble must sit horizontally centred on the speaker.");
            Assert.AreEqual(expected.y, bubble.transform.position.y, 0.0001f,
                "The bubble must float _yOffset (1.5) above the entity pivot — at offset 0 it " +
                "would be drawn inside the sprite.");
        }

        [Test]
        public void LateUpdate_TargetMoved_ReFollowsOnEveryTick()
        {
            var target = NewGo("Speaker");
            var bubble = CreateBubble(target.transform);
            Tick(bubble);

            target.transform.position = new Vector3(-7.5f, 3.25f, 0f);
            Tick(bubble);

            Assert.AreEqual(-7.5f, bubble.transform.position.x, 0.0001f,
                "The follow position must be recomputed each tick, not cached at Initialize.");
            Assert.AreEqual(3.25f + DefaultYOffset, bubble.transform.position.y, 0.0001f,
                "A walking NPC must drag its bubble along, keeping the same vertical offset.");
        }

        [Test]
        public void LateUpdate_AfterTargetDestroyed_KeepsLastPositionWithoutThrowing()
        {
            var target = NewGo("Speaker");
            target.transform.position = new Vector3(1f, 1f, 0f);
            var bubble = CreateBubble(target.transform);
            bubble.PushBubble("Adios");
            Tick(bubble);
            Vector3 lastKnown = bubble.transform.position;

            Object.DestroyImmediate(target);
            _created.Remove(target);

            Assert.DoesNotThrow(() => Tick(bubble),
                "A destroyed follow target must be caught by the Unity fake-null check; an NPC " +
                "dying mid-conversation would otherwise spam MissingReferenceException every " +
                "frame for the rest of the session.");
            Assert.AreEqual(lastKnown, bubble.transform.position,
                "With no target left the bubble must freeze where it was, not snap to origin.");
        }

        [Test]
        public void LateUpdate_WithNullTargetAndNoBubbles_LeavesPositionUntouched()
        {
            var bubble = CreateBubble(null);
            bubble.transform.position = new Vector3(9f, 9f, 0f);

            Assert.DoesNotThrow(() => Tick(bubble),
                "Initialize(null) is reachable when the speaker transform is missing; an empty " +
                "bubble list plus a null target must still tick harmlessly.");
            Assert.AreEqual(new Vector3(9f, 9f, 0f), bubble.transform.position,
                "With no target the transform must be left exactly where it was placed.");
        }

        [Test]
        public void LateUpdate_WithMainCamera_BillboardsTowardsCameraForward()
        {
            var cam = NewGo("TestMainCamera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.transform.rotation = Quaternion.Euler(25f, 40f, 0f);

            var bubble = CreateBubble(NewGo("Speaker").transform);
            Tick(bubble);

            var main = Camera.main;
            Assert.IsTrue(main != null,
                "Sanity: a MainCamera-tagged camera must be resolvable for this test.");
            Assert.AreEqual(main.transform.forward.x, bubble.transform.forward.x, 0.001f,
                "The world-space canvas must face the active camera, otherwise the text is " +
                "rendered edge-on or mirrored.");
            Assert.AreEqual(main.transform.forward.z, bubble.transform.forward.z, 0.001f,
                "Billboarding must align the whole forward vector, not just one axis.");
        }

        // ── LateUpdate: lifetime ─────────────────────────────────────────────

        [Test]
        public void LateUpdate_WithinTtl_KeepsBubblesAliveAcrossRepeatedTicks()
        {
            var bubble = CreateBubble(NewGo("Speaker").transform);
            bubble.PushBubble("Mensaje uno", 2500);
            bubble.PushBubble("Mensaje dos", 2800);

            Assert.DoesNotThrow(() =>
            {
                Tick(bubble);
                Tick(bubble);
                Tick(bubble);
            }, "Ticking inside the TTL window must not destroy anything. A regression that " +
               "treats ttlMs as an absolute time (or drops the /1000 conversion) expires " +
               "bubbles on the first frame, which in edit mode surfaces as an illegal " +
               "Object.Destroy call.");

            Assert.AreEqual(2, bubble.transform.childCount,
                "Both bubbles must still exist well inside their 2.5s / 2.8s TTL.");
            Assert.AreEqual(1f, BubbleRectAt(bubble, 0).GetComponent<CanvasGroup>().alpha, 0.001f,
                "The fade only starts at 70% of the TTL, so alpha must still be fully opaque " +
                "immediately after the push.");
        }
    }
}
