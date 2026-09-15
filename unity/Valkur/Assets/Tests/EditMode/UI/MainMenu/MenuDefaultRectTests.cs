using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu
{
    /// <summary>
    /// Nothing the menu draws may be left at Unity's DEFAULT rect.
    ///
    /// <para><b>What it caught, and what it could not.</b> A new RectTransform arrives 100x100
    /// with centred anchors, and that is a size nobody chose. Twelve images in the class selector
    /// kept it — two per stat bar, six bars per card — so each card wore a stack of cream slabs
    /// over the class name and over the words Vida / Ataque / Armadura. Every structural
    /// assertion about that panel was GREEN, because uGUI performs no layout in Edit Mode; what
    /// found it was a rendered frame.</para>
    ///
    /// <para>So this test does not claim to check a layout. It checks the one thing that IS
    /// exact without a layout pass: whether the builder ever STATED a geometry. The default rect
    /// is a fingerprint — centred anchors AND exactly 100x100 — and a widget wearing it is a
    /// widget whose size was never a decision. A child of a layout group is exempt: the group
    /// writes the rect and the builder is right not to.</para>
    /// </summary>
    public class MenuDefaultRectTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void EveryImageTheKitBuilds_StatesItsOwnGeometry()
        {
            var style = MenuStyle.Active;
            var art = MenuArt.Get(style);
            _root = new GameObject("MenuDefaultRectRoot", typeof(RectTransform));
            var parent = (RectTransform)_root.transform;
            parent.sizeDelta = new Vector2(400f, 200f);

            var img = MenuUIKit.Sprite("Probe", parent, art.SliderFill, Color.white);
            var rt = img.rectTransform;
            Assert.IsFalse(IsUnityDefault(rt),
                "MenuUIKit.Sprite left Unity's 100x100 centred default: an image sized by nobody");
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
        }

        [Test]
        public void NoWidgetInTheWholeMenu_WearsTheDefaultRect()
        {
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            _root = new GameObject("MainMenuDefaultRectHost");
            var menu = _root.AddComponent<MainMenuUI>();
            // Start by REFLECTION, or `_canvasTransform` is null, `EnsureScreenBuilt` returns on
            // its first line and this test walks an EMPTY hierarchy — green, and about nothing.
            typeof(MainMenuUI).GetMethod(
                "Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(menu, null);
            menu.BuildAllScreensForTests();

            var inspected = 0;
            var offenders = new List<string>();
            foreach (var rt in _root.GetComponentsInChildren<RectTransform>(true))
            {
                if (!IsUnityDefault(rt)) continue;
                // A layout group owns its children's rects; the builder is right not to write one.
                var parent = rt.parent as RectTransform;
                if (parent != null && parent.GetComponent<LayoutGroup>() != null) continue;
                if (rt.GetComponent<Graphic>() == null) continue;
                offenders.Add(Path(rt));
            }
            foreach (var g in _root.GetComponentsInChildren<Graphic>(true)) { if (g != null) inspected++; }

            // A guard against the vacuous pass: an empty hierarchy has no offenders either.
            Assert.Greater(inspected, 100,
                "the menu built almost nothing, so this test would be green about nothing");
            Assert.IsEmpty(offenders,
                "these draw at Unity's 100x100 centred default, i.e. at a size nobody chose: "
                + string.Join(" | ", offenders));
        }

        /// <summary>
        /// The fingerprint of a rect nobody wrote. BOTH halves are needed: a deliberate 100x100
        /// badge is normal, and so is a centred anchor — only the pair says "untouched".
        /// </summary>
        private static bool IsUnityDefault(RectTransform rt)
        {
            var half = new Vector2(0.5f, 0.5f);
            return rt.anchorMin == half && rt.anchorMax == half
                   && Mathf.Approximately(rt.sizeDelta.x, 100f)
                   && Mathf.Approximately(rt.sizeDelta.y, 100f);
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
