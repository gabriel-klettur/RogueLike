using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Regression tests for <see cref="SelectableArrayPreGrow"/> — the workaround
    /// that defends Valkur against UGUI's
    /// <c>IndexOutOfRangeException</c> at
    /// <c>Selectable.OnEnable()</c> line 518 when Domain Reload is OFF.
    ///
    /// ── Test safety note ────────────────────────────────────────────────
    /// EditMode tests share a single process with the live editor — the test
    /// runner UI, project browser, and inspector are all populated with UGUI
    /// <see cref="Selectable"/> instances whose <c>m_CurrentIndex</c> points
    /// into <c>UnityEngine.UI.Selectable.s_Selectables</c>. If a test
    /// reflectively REPLACES that static array or zeroes the count, those
    /// pre-existing instances would underflow / IOOR the next time their
    /// OnDisable runs. Therefore this fixture <b>never installs synthetic
    /// drift state</b> — it only inspects the live state, invokes the reset,
    /// and verifies the post-condition. The "Reset recovers from drift &gt;
    /// array length" guarantee is documented in code (see
    /// <see cref="SelectableArrayPreGrow"/>'s XML doc) and is the property
    /// the runtime hook delivers at <c>SubsystemRegistration</c> time —
    /// which is exactly when no Selectables are alive and synthetic drift
    /// IS safe.
    /// </summary>
    [TestFixture]
    public class SelectableArrayPreGrowTests
    {
        private FieldInfo _arrField;
        private FieldInfo _countField;
        private MethodInfo _resetMethod;

        [SetUp]
        public void SetUp()
        {
            var t = typeof(Selectable);
            _arrField = t.GetField("s_Selectables",
                BindingFlags.NonPublic | BindingFlags.Static);
            _countField = t.GetField("s_SelectableCount",
                BindingFlags.NonPublic | BindingFlags.Static);
            _resetMethod = typeof(SelectableArrayPreGrow).GetMethod("ResetStaticState",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(_arrField, "UGUI Selectable.s_Selectables field not found via reflection.");
            Assert.IsNotNull(_countField, "UGUI Selectable.s_SelectableCount field not found via reflection.");
            Assert.IsNotNull(_resetMethod, "SelectableArrayPreGrow.ResetStaticState not found via reflection.");
        }

        // ── Field discovery -----------------------------------------------

        [Test]
        public void SelectablesFieldIsTypedAsSelectableArray()
        {
            Assert.AreEqual(typeof(Selectable[]), _arrField.FieldType,
                "s_Selectables should be a Selectable[] — UGUI signature changed.");
        }

        [Test]
        public void SelectableCountFieldIsTypedAsInt()
        {
            Assert.AreEqual(typeof(int), _countField.FieldType,
                "s_SelectableCount should be an int — UGUI signature changed.");
        }

        [Test]
        public void ResetStaticStateMethod_HasRuntimeInitializeAttribute()
        {
            // The runtime hook MUST be present — otherwise the workaround
            // never fires at Play start and the entire defense disappears.
            var attr = _resetMethod.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(attr,
                "ResetStaticState must carry [RuntimeInitializeOnLoadMethod] — " +
                "without it the workaround never runs.");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attr.loadType,
                "ResetStaticState must fire at SubsystemRegistration so no " +
                "Selectable has registered yet when the reset runs.");
        }

        // ── Post-reset state (against the LIVE editor state) --------------
        //
        // These tests do not poison the live editor's static state — they
        // simply invoke the reset and verify the resulting state matches the
        // documented contract. Since the reset is idempotent and produces a
        // strictly more capacious state than before (1024-slot fresh array,
        // count = 0), running it inside an EditMode session at most replaces
        // the array reference; live editor Selectables that still reference
        // the OLD array via m_CurrentIndex are not destroyed by the
        // reset (their OnDisable would underflow against the new count).
        //
        // To stay safe across the suite, every test below saves the
        // live (array, count) pair and restores it after invoking the reset.

        [Test]
        public void Reset_LeavesArrayLength_AtLeastInitialCapacity()
        {
            var savedArr = (Selectable[])_arrField.GetValue(null);
            int savedCount = (int)_countField.GetValue(null);
            try
            {
                _resetMethod.Invoke(null, null);
                var arr = (Selectable[])_arrField.GetValue(null);
                Assert.GreaterOrEqual(arr.Length, SelectableArrayPreGrow.InitialCapacity,
                    "Array should be at least InitialCapacity after reset.");
            }
            finally
            {
                _arrField.SetValue(null, savedArr);
                _countField.SetValue(null, savedCount);
            }
        }

        [Test]
        public void Reset_ZeroesTheCount()
        {
            var savedArr = (Selectable[])_arrField.GetValue(null);
            int savedCount = (int)_countField.GetValue(null);
            try
            {
                _resetMethod.Invoke(null, null);
                Assert.AreEqual(0, (int)_countField.GetValue(null),
                    "Count must be 0 after reset — this is the only way to recover from drift > array length.");
            }
            finally
            {
                _arrField.SetValue(null, savedArr);
                _countField.SetValue(null, savedCount);
            }
        }

        [Test]
        public void Reset_ReplacesArrayInstance_AllSlotsNull()
        {
            var savedArr = (Selectable[])_arrField.GetValue(null);
            int savedCount = (int)_countField.GetValue(null);
            try
            {
                _resetMethod.Invoke(null, null);
                var arr = (Selectable[])_arrField.GetValue(null);
                Assert.AreNotSame(savedArr, arr,
                    "Reset should replace the array reference, not mutate the old one.");
                for (int i = 0; i < arr.Length; i++)
                    Assert.IsNull(arr[i], $"Fresh array slot {i} should be null.");
            }
            finally
            {
                _arrField.SetValue(null, savedArr);
                _countField.SetValue(null, savedCount);
            }
        }

        [Test]
        public void Reset_IsIdempotent()
        {
            var savedArr = (Selectable[])_arrField.GetValue(null);
            int savedCount = (int)_countField.GetValue(null);
            try
            {
                _resetMethod.Invoke(null, null);
                _resetMethod.Invoke(null, null);
                _resetMethod.Invoke(null, null);
                var arr = (Selectable[])_arrField.GetValue(null);
                Assert.GreaterOrEqual(arr.Length, SelectableArrayPreGrow.InitialCapacity);
                Assert.AreEqual(0, (int)_countField.GetValue(null),
                    "Count should remain 0 after repeated resets.");
            }
            finally
            {
                _arrField.SetValue(null, savedArr);
                _countField.SetValue(null, savedCount);
            }
        }
    }
}
