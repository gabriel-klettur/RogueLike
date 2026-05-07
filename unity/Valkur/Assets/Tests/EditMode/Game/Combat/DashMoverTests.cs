using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for <c>DashCasterMover</c> and <c>DashTrailMover</c> —
    /// both are <c>internal</c> MonoBehaviours defined inside DashExecutor.cs.
    ///
    /// Both expose a <c>public void Tick(float dt)</c> test seam — Update simply
    /// delegates to it with <c>Time.deltaTime</c>. EditMode tests call Tick with
    /// dt = 0 (and inject the desired _age via reflection) so the lerp / snap /
    /// stop-emitting logic runs deterministically without picking up whatever
    /// non-zero Time.deltaTime the editor session reports.
    /// </summary>
    public class DashMoverTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ────────────────────────────────────────────

        private static System.Type GetType(string typeName)
        {
            var asm = typeof(Valkur.Gameplay.Spells.DashExecutor).Assembly;
            var t   = asm.GetType($"Valkur.Gameplay.Spells.{typeName}");
            Assert.IsNotNull(t, $"Type 'Valkur.Gameplay.Spells.{typeName}' not found");
            return t;
        }

        private static void SetField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            var f = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {target.GetType().Name}");
            return (T)f.GetValue(target);
        }

        private static void CallUpdate(Component c)
        {
            // Invokes the test seam Tick(float) with dt = 0 so the lerp uses
            // exactly whatever _age the test injected, ignoring the editor's
            // current Time.deltaTime. Production Update wraps Tick.
            var m = c.GetType().GetMethod("Tick",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, $"Tick(float) not found on {c.GetType().Name}");
            m.Invoke(c, new object[] { 0f });
        }

        // ── DashCasterMover ───────────────────────────────────────────────

        [Test]
        public void DashCasterMover_Init_SetsPositionToFrom()
        {
            _go = new GameObject("CasterMoverTest");
            var type = GetType("DashCasterMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(1f, 2f, 0f);
            var to   = new Vector3(5f, 6f, 0f);

            // Call Init via reflection
            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, 0.5f });

            Assert.AreEqual(from, _go.transform.position,
                "After Init, position must equal _from");
        }

        [Test]
        public void DashCasterMover_Init_StoresDuration_ClampedToMinimum()
        {
            _go = new GameObject("CasterMoverTest");
            var type = GetType("DashCasterMover");
            var mover = _go.AddComponent(type) as Component;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { Vector3.zero, Vector3.one, 0f });

            float duration = GetField<float>(mover, "_duration");
            Assert.GreaterOrEqual(duration, 0.01f,
                "Duration must be clamped to at least 0.01f to prevent div-by-zero");
        }

        [Test]
        public void DashCasterMover_Update_AtHalfTime_IsAtMidpoint()
        {
            _go = new GameObject("CasterMoverTest");
            var type = GetType("DashCasterMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(4f, 0f, 0f);
            float dur = 1f;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, dur });

            // Inject _age = half of duration (Time.deltaTime = 0 in EditMode)
            SetField(mover, "_age", dur * 0.5f);
            CallUpdate(mover);

            var mid = Vector3.Lerp(from, to, 0.5f);
            Assert.AreEqual(mid.x, _go.transform.position.x, 0.001f,
                "At t=0.5 position must be at the midpoint between from and to");
        }

        [Test]
        public void DashCasterMover_Update_AtFullTime_SnapsToDestination()
        {
            _go = new GameObject("CasterMoverTest");
            var type = GetType("DashCasterMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(8f, 3f, 0f);
            float dur = 0.3f;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, dur });

            // Inject _age >= duration so t >= 1
            SetField(mover, "_age", dur);
            CallUpdate(mover);

            Assert.AreEqual(to.x, _go.transform.position.x, 0.001f,
                "At t=1 position must snap exactly to _to");
            Assert.AreEqual(to.y, _go.transform.position.y, 0.001f,
                "At t=1 position.y must snap exactly to _to.y");
        }

        [Test]
        public void DashCasterMover_Update_BeforeFullTime_PositionIsStillInterpolated()
        {
            _go = new GameObject("CasterMoverTest");
            var type = GetType("DashCasterMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(4f, 0f, 0f);
            float dur = 1f;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, dur });

            SetField(mover, "_age", 0.3f); // t = 0.3, well below 1
            CallUpdate(mover);

            float x = _go.transform.position.x;
            // x should be between from.x and to.x (exclusive)
            Assert.Greater(x, from.x, "Position must have advanced past _from");
            Assert.Less(x, to.x, "Position must not have reached _to yet at t=0.3");
        }

        // ── DashTrailMover ────────────────────────────────────────────────

        [Test]
        public void DashTrailMover_Init_SetsPositionToFrom()
        {
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(2f, 3f, 0f);
            var to   = new Vector3(9f, 3f, 0f);

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, 0.18f });

            Assert.AreEqual(from, _go.transform.position,
                "After Init, DashTrailMover position must equal _from");
        }

        [Test]
        public void DashTrailMover_Init_StoresDuration_ClampedToMinimum()
        {
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { Vector3.zero, Vector3.one, -5f });

            float duration = GetField<float>(mover, "_duration");
            Assert.GreaterOrEqual(duration, 0.01f,
                "Negative duration must be clamped to at least 0.01f");
        }

        [Test]
        public void DashTrailMover_Update_AtHalfTime_IsAtMidpoint()
        {
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(6f, 0f, 0f);
            float dur = 1f;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, dur });

            SetField(mover, "_age", dur * 0.5f);
            CallUpdate(mover);

            Assert.AreEqual(3f, _go.transform.position.x, 0.001f,
                "At t=0.5, DashTrailMover must be at the midpoint");
        }

        [Test]
        public void DashTrailMover_Update_AtFullTime_IsAtDestination()
        {
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            var from = Vector3.zero;
            var to   = new Vector3(5f, 2f, 0f);
            float dur = 0.18f;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { from, to, dur });

            SetField(mover, "_age", dur);
            CallUpdate(mover);

            Assert.AreEqual(to.x, _go.transform.position.x, 0.001f,
                "At t=1, DashTrailMover must be at the destination x");
            Assert.AreEqual(to.y, _go.transform.position.y, 0.001f,
                "At t=1, DashTrailMover must be at the destination y");
        }

        [Test]
        public void DashTrailMover_Update_AtFullTime_SetsStoppedFlag()
        {
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { Vector3.zero, Vector3.right, 0.18f });

            SetField(mover, "_age", 0.18f); // t = 1
            CallUpdate(mover);

            bool stopped = GetField<bool>(mover, "_stopped");
            Assert.IsTrue(stopped, "After t >= 1 _stopped must be set to true");
        }

        [Test]
        public void DashTrailMover_Update_MultipleCallsAtFullTime_DoesNotSetStoppedMultipleTimes()
        {
            // _stopped is a one-shot flag — verify it stays true and doesn't toggle
            _go = new GameObject("TrailMoverTest");
            var type = GetType("DashTrailMover");
            var mover = _go.AddComponent(type) as Component;

            type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(mover, new object[] { Vector3.zero, Vector3.right * 3f, 0.2f });

            SetField(mover, "_age", 0.2f);
            CallUpdate(mover); // first Update past t=1

            SetField(mover, "_age", 0.4f);
            CallUpdate(mover); // second Update past t=1

            // _stopped must remain true (no toggling)
            Assert.IsTrue(GetField<bool>(mover, "_stopped"),
                "_stopped must remain true on subsequent updates past t=1");
        }
    }
}
