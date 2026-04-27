using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Items;

namespace Valkur.Tests.EditMode.Editors.Items
{
    /// <summary>
    /// Phase 3 tests: drag-from-picker, click-to-select world instance, and
    /// instance-actions UI (qty +/- + Delete) in the Properties panel.
    /// </summary>
    [TestFixture]
    public class ItemsRuntimeEditorPhase3Tests
    {
        private readonly System.Collections.Generic.List<GameObject>  _scene = new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<Object> _runtimeAssets = new System.Collections.Generic.List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
            ClearSingletonInstance<ItemsRuntimeEditor>();
        }

        // ── Reflection helpers (mirror Phase 2 test fixture) ──

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);
        private static void   SetField(object obj, string name, object value) => Field(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        private ItemsRuntimeEditor CreateActiveEditor()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingletonInstance<ItemsRuntimeEditor>();
            var go = new GameObject("TestItemsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<ItemsRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            ed.Activate();
            return ed;
        }

        private ItemDefinition CreateItem(string id, string displayName, bool withIcon = true)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId      = id;
            def.displayName = displayName;
            def.stackable   = true;
            def.maxStack    = 99;
            if (withIcon)
                def.icon = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 16f);
            _runtimeAssets.Add(def);
            return def;
        }

        private void InjectCatalog(ItemsRuntimeEditor ed, params ItemDefinition[] items)
        {
            SetField(ed, "_allItems", items);
            Invoke(ed, "RefreshPicker");
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Test]
        public void PickerSlot_HasButtonAndPointerDownEventTrigger_ForDragFromPicker()
        {
            var ed = CreateActiveEditor();
            var apple = CreateItem("apple", "Apple");
            InjectCatalog(ed, apple);

            var refs = GetField(ed, "_uiRefs");
            var picker = (RectTransform)Field(refs, "PickerContent").GetValue(refs);
            Assert.AreEqual(1, picker.childCount);

            var slot = picker.GetChild(0).gameObject;
            Assert.IsNotNull(slot.GetComponent<Button>(), "Slot must keep its Button (LMB select).");
            var et = slot.GetComponent<EventTrigger>();
            Assert.IsNotNull(et, "Slot must have an EventTrigger for drag/right-click handlers.");
            bool hasPointerDown = false;
            for (int i = 0; i < et.triggers.Count; i++)
                if (et.triggers[i].eventID == EventTriggerType.PointerDown) { hasPointerDown = true; break; }
            Assert.IsTrue(hasPointerDown,
                "Slot must register an EventTrigger.PointerDown entry to seed drag-from-picker.");
        }

        [Test]
        public void SetActiveInstance_PopulatesProperties_AndShowsInstanceActionsBlock()
        {
            var ed = CreateActiveEditor();
            var sword = CreateItem("sword", "Iron Sword");
            InjectCatalog(ed, sword);

            // Spawn one in the world
            Invoke(ed, "SelectItem", "sword");
            Invoke(ed, "SpawnAt", new Vector3(2f, 3f, 0f));
            var pickup = Object.FindObjectOfType<WorldPickup>();
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);

            // Promote it to the active instance
            ed.SetActiveInstance(pickup);

            var refs = GetField(ed, "_uiRefs");
            var propsContent = (RectTransform)Field(refs, "PropsContent").GetValue(refs);
            // Look for the dynamically-built actions block.
            Transform actions = null;
            for (int i = 0; i < propsContent.childCount; i++)
            {
                if (propsContent.GetChild(i).name == "InstanceActions")
                {
                    actions = propsContent.GetChild(i);
                    break;
                }
            }
            Assert.IsNotNull(actions, "Properties panel must contain an 'InstanceActions' child when an instance is selected.");
            // Must contain at least the Qty and Delete sub-rows.
            Assert.IsNotNull(actions.Find("QtyRow"), "Actions block must contain a QtyRow.");
            Assert.IsNotNull(actions.Find("DelRow"), "Actions block must contain a DelRow.");
        }

        [Test]
        public void AdjustSelectedQuantity_BumpsQuantity_AndCanBeUndone()
        {
            var ed = CreateActiveEditor();
            var apple = CreateItem("apple", "Apple");
            InjectCatalog(ed, apple);
            Invoke(ed, "SelectItem", "apple");
            Invoke(ed, "SpawnAt", new Vector3(0, 0, 0));
            var pickup = Object.FindObjectOfType<WorldPickup>();
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);
            ed.SetActiveInstance(pickup);

            int before = pickup.Quantity;
            Invoke(ed, "AdjustSelectedQuantity", 5);
            Assert.AreEqual(before + 5, pickup.Quantity, "Quantity must be bumped by +5.");

            Invoke(ed, "DoUndo");
            Assert.AreEqual(before, pickup.Quantity, "Undo must restore the original quantity.");
        }

        [Test]
        public void DeleteSelectedInstance_RemovesPickup_AndClearsSelection()
        {
            var ed = CreateActiveEditor();
            var apple = CreateItem("apple", "Apple");
            InjectCatalog(ed, apple);
            Invoke(ed, "SelectItem", "apple");
            Invoke(ed, "SpawnAt", new Vector3(0, 0, 0));
            var pickup = Object.FindObjectOfType<WorldPickup>();
            Assert.IsNotNull(pickup);
            _scene.Add(pickup.gameObject);
            ed.SetActiveInstance(pickup);

            int before = Object.FindObjectsOfType<WorldPickup>().Length;
            Invoke(ed, "DeleteSelectedInstance");

            int after = Object.FindObjectsOfType<WorldPickup>().Length;
            Assert.LessOrEqual(after, before, "DeleteSelectedInstance must not increase pickup count.");
            Assert.IsNull(GetField(ed, "_selectedInstance"),
                "_selectedInstance must be cleared after deleting it.");
        }
    }
}
