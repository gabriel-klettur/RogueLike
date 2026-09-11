using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Editors.MultiSelect
{
    /// <summary>
    /// Double-clicking a selected item opens ITS editor, and Escape there comes back.
    ///
    /// <para>The gesture itself needs live editors, live content and a pointer, so what is
    /// pinned here is the two halves that decide whether it can work at all: the RETURN TRAIL
    /// on <see cref="GameEditorManager"/>, which is pure state and where every way this can go
    /// wrong lives, and the DECLARATIONS — a focus seam per editor and one launcher branch —
    /// which are the parts that fail silently if they are ever moved.</para>
    /// </summary>
    [TestFixture]
    public class SelectionOpenEditorTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private GameObject _host;
        private GameEditorManager _mgr;

        /// <summary>A minimal editor. <c>selfNotify</c> reproduces the real lifecycle: an
        /// editor's own <c>Deactivate</c> calls <c>NotifyDeactivated</c>, which is what clears
        /// the trail — and is precisely why the target must be armed AFTER the switch rather
        /// than before it.</summary>
        private sealed class FakeEditor : GameEditorManager.IGameEditor
        {
            private readonly GameEditorManager _mgr;
            private readonly bool _selfNotify;

            public FakeEditor(string name, GameEditorManager mgr = null, bool selfNotify = false)
            { EditorName = name; _mgr = mgr; _selfNotify = selfNotify; }

            public string EditorName { get; }
            public bool   IsActive { get; private set; }

            public void Activate() { IsActive = true; }
            public void Deactivate()
            {
                IsActive = false;
                if (_selfNotify && _mgr != null) _mgr.NotifyDeactivated(this);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("[GameEditorManagerTestHost]");
            _mgr  = _host.AddComponent<GameEditorManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
        }

        private FakeEditor Register(string name, bool selfNotify = false)
        {
            var e = new FakeEditor(name, _mgr, selfNotify);
            _mgr.Register(e);
            return e;
        }

        // ── The return trail ─────────────────────────────────────────────────

        [Test]
        public void OpenWithReturn_ArmsTheTrail_AndOpensTheTarget()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings");

            _mgr.OpenExclusive(tool);
            _mgr.OpenExclusive(target, tool);

            Assert.That(target.IsActive, Is.True, "the target must actually open");
            Assert.That(_mgr.ReturnTarget, Is.SameAs(tool));
        }

        /// <summary>
        /// THE ORDERING TRAP, and the only reason this fixture builds a self-notifying fake.
        /// Opening tears the previous editor down, and its own Deactivate ends the trail —
        /// correctly. Arming BEFORE the switch therefore arms a pointer the very next line
        /// erases, and the feature never fires once in the shipped game while every other test
        /// here stays green.
        /// </summary>
        [Test]
        public void OpenWithReturn_SurvivesThePreviousEditorNotifyingItsOwnDeactivation()
        {
            var tool   = Register("Seleccion", selfNotify: true);
            var target = Register("Buildings");

            _mgr.OpenExclusive(tool);
            _mgr.OpenExclusive(target, tool);

            Assert.That(_mgr.ReturnTarget, Is.SameAs(tool),
                "the trail must be armed after the switch, not before it");
        }

        [Test]
        public void TheTrail_IsConsumedExactlyOnce()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings");
            _mgr.OpenExclusive(target, tool);

            Assert.That(_mgr.TryConsumeReturnTarget(out var back), Is.True);
            Assert.That(back, Is.SameAs(tool));
            Assert.That(_mgr.TryConsumeReturnTarget(out _), Is.False,
                "a second Escape must behave exactly as it always has");
        }

        [Test]
        public void AnOrdinaryOpen_ClearsTheTrail()
        {
            var tool  = Register("Seleccion");
            var a     = Register("Buildings");
            var other = Register("Tile Editor");

            _mgr.OpenExclusive(a, tool);
            _mgr.OpenExclusive(other);

            Assert.That(_mgr.ReturnTarget, Is.Null,
                "arriving somewhere by another route means the old target describes nowhere the author has been");
        }

        [Test]
        public void ClosingToGameplay_EndsTheTrail()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings");
            _mgr.OpenExclusive(target, tool);

            _mgr.ToggleExclusive(target);

            Assert.That(_mgr.ReturnTarget, Is.Null);
        }

        [Test]
        public void CloseAll_EndsTheTrail()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings");
            _mgr.OpenExclusive(target, tool);

            _mgr.CloseAll();

            Assert.That(_mgr.ReturnTarget, Is.Null);
        }

        [Test]
        public void AnEditorThatClosesItself_EndsTheTrail()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings", selfNotify: true);
            _mgr.OpenExclusive(target, tool);

            target.Deactivate();

            Assert.That(_mgr.ReturnTarget, Is.Null);
        }

        [Test]
        public void ReturningToTheEditorJustOpened_IsRefused()
        {
            var target = Register("Buildings");
            _mgr.OpenExclusive(target, target);

            Assert.That(_mgr.ReturnTarget, Is.Null,
                "Escape into a no-op reads as a frozen key");
        }

        [Test]
        public void AnUnregisteredTarget_IsNeverReopened()
        {
            var tool   = Register("Seleccion");
            var target = Register("Buildings");
            _mgr.OpenExclusive(target, tool);

            _mgr.Unregister(tool);

            Assert.That(_mgr.TryConsumeReturnTarget(out var back), Is.False);
            Assert.That(back, Is.Null, "a torn-down editor would be reopened as a MissingReference");
        }

        // ── The declarations ─────────────────────────────────────────────────

        [Test]
        public void EveryDomain_CanOpenItsOwnEditor()
        {
            var iface = Type.GetType("Valkur.Gameplay.Editors.MultiSelect.ISelectionDomain, Valkur.Gameplay");
            Assert.That(iface, Is.Not.Null);

            var open = iface.GetMethod("OpenEditorFor");
            Assert.That(open, Is.Not.Null, "the domain is what knows which editor an item belongs to");

            var domains = iface.Assembly.GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && iface.IsAssignableFrom(t))
                .ToArray();
            Assert.That(domains.Length, Is.EqualTo(3), "buildings, particles, lights");
        }

        /// <summary>
        /// Each editor exposes a FOCUS seam, so opening lands ON the double-clicked item rather
        /// than merely opening a panel the author then has to hunt in. They are private-by-
        /// partial, so nothing but a reflection check can see one go missing.
        /// </summary>
        [TestCase("Valkur.Gameplay.Buildings.BuildingsRuntimeEditor")]
        [TestCase("Valkur.Gameplay.VFX.ParticlesRuntimeEditor")]
        [TestCase("Valkur.Gameplay.World.LightingRuntimeEditor")]
        public void EveryTargetEditor_HasItsFocusSeam(string typeName)
        {
            var t = Type.GetType(typeName + ", Valkur.Gameplay");
            Assert.That(t, Is.Not.Null, typeName);
            Assert.That(t.GetMethod("MultiSelectFocus", Any), Is.Not.Null,
                typeName + " must be focusable from the Selection tool");
        }

        /// <summary>
        /// The launcher must consult the trail BEFORE it opens itself, and this is a source
        /// check because the branch sits inside an Update guarded by a live hotkey read. Escape
        /// has one reader per press: if the launcher opens first, the trail is dead code and
        /// the author lands at the menu with their selection stranded one screen back.
        /// </summary>
        [Test]
        public void TheLauncher_ConsultsTheTrail_BeforeOpeningItself()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/General/GeneralEditorManager.cs");
            Assert.That(File.Exists(path), Is.True, path);

            string src   = File.ReadAllText(path);
            int    trail = src.IndexOf("TryConsumeReturnTarget", StringComparison.Ordinal);
            int    self  = src.IndexOf("mgr.OpenExclusive(this)", StringComparison.Ordinal);

            Assert.That(trail, Is.GreaterThan(-1), "the launcher must read the return trail");
            Assert.That(self,  Is.GreaterThan(-1), "the launcher must still open itself otherwise");
            Assert.That(trail, Is.LessThan(self),  "the trail is consulted first, or it can never fire");
        }
    }
}
