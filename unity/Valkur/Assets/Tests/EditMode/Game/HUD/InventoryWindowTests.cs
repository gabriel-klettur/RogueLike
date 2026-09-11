using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Inventory;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The inventory window, built and ticked in Edit Mode. Pins the rules the 2026-09-11
    /// rebuild exists for (<c>.github/INVENTORY_HUD_BEAUTY_AUDIT_2026-09-11.md</c>): the texel
    /// grid, the dialect (no editor theme), the typed paper doll, rarity as shape, the window
    /// opening beside the minimap rather than over it, pickups found by difference and the
    /// window's own moves never announced as finds, refusals that say why, and a mote budget.
    /// </summary>
    [TestFixture]
    public class InventoryWindowTests
    {
        private GameObject _player;
        private Inventory _inventory;
        private GameObject _uiGo;
        private InventoryUI _ui;
        private readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _player = new GameObject("Player");
            _inventory = _player.AddComponent<Inventory>();
            _inventory.Initialize(Inventory.DefaultBagCapacity);
            _player.AddComponent<Experience>();
            _uiGo = new GameObject("InventoryUI");
            _ui = _uiGo.AddComponent<InventoryUI>();
            _ui.BuildFor(_player);
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui != null) _ui.TeardownForTests();
            if (_uiGo != null) Object.DestroyImmediate(_uiGo);
            if (_player != null) Object.DestroyImmediate(_player);
            foreach (var a in _assets) if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private ItemDefinition Item(string id, EquipSlot slot = EquipSlot.None, ItemRarity rarity = ItemRarity.Common,
                                    bool stackable = false, int maxStack = 1)
        {
            var it = ScriptableObject.CreateInstance<ItemDefinition>();
            it.itemId = id;
            it.displayName = id;
            it.equipSlot = slot;
            it.rarity = rarity;
            it.stackable = stackable;
            it.maxStack = maxStack;
            _assets.Add(it);
            return it;
        }

        private void Run(float seconds, float dt = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += dt) _ui.Tick(dt);
        }

        // ── Grid ─────────────────────────────────────────────────────────

        [Test]
        public void EveryRectInThePixelSpace_SitsOnWholeTexels()
        {
            var pixels = _ui.Panel.Find("Pixels");
            Assert.IsNotNull(pixels);
            int checkedRects = 0;
            foreach (var rt in pixels.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == pixels || rt.anchorMin != rt.anchorMax) continue;
                var p = rt.anchoredPosition;
                var s = rt.sizeDelta;
                Assert.AreEqual(Mathf.Round(p.x), p.x, 0.0001f, rt.name + " x off the texel grid");
                Assert.AreEqual(Mathf.Round(p.y), p.y, 0.0001f, rt.name + " y off the texel grid");
                Assert.AreEqual(Mathf.Round(s.x), s.x, 0.0001f, rt.name + " width off the texel grid");
                Assert.AreEqual(Mathf.Round(s.y), s.y, 0.0001f, rt.name + " height off the texel grid");
                Assert.AreEqual(Vector2.zero, rt.pivot, rt.name + " must pivot bottom-left");
                checkedRects++;
            }
            Assert.Greater(checkedRects, 150, "Vacuous: the walk found almost nothing to check.");
        }

        [Test]
        public void TheWindowHasSpritesNotFlatRectangles()
        {
            // The old window drew 76 Images and exactly one sprite.
            int images = 0, withSprite = 0;
            foreach (var img in _ui.Panel.GetComponentsInChildren<Image>(true))
            {
                // Invisible hit areas, and the raw-sprite fallbacks that only switch on without a GPU.
                if (img.color.a <= 0f || !img.enabled) continue;
                images++;
                if (img.sprite != null) withSprite++;
            }
            Assert.Greater(images, 0);
            Assert.GreaterOrEqual(withSprite, images - 1,
                "Every visible Image draws a piece of the kit, not a flat colour.");
            Assert.AreEqual(0, _ui.Panel.GetComponentsInChildren<Outline>(true).Length,
                "No uGUI Outline: the frames are drawn, not faked with four copies of the quad.");
        }

        // ── Dialect ──────────────────────────────────────────────────────

        [Test]
        public void NoInventoryFile_WearsTheEditorTheme()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Inventory");
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(f);
                Assert.IsFalse(src.Contains("TileEditorTheme"), Path.GetFileName(f) + " wears the editor theme (TileEditorTheme).");
                Assert.IsFalse(src.Contains("TileEditorUIHelpers"), Path.GetFileName(f) + " wears the editor theme (TileEditorUIHelpers).");
                Assert.IsFalse(src.Contains("EditorUIHelpers"), Path.GetFileName(f) + " wears the editor theme (EditorUIHelpers).");
                Assert.IsFalse(src.Contains("AssetDatabase"), Path.GetFileName(f) +
                    ": runtime sprites must not come from AssetDatabase — it does not exist in a build.");
            }
        }

        [Test]
        public void NoPlayerFacingString_NamesALiteralKey()
        {
            // The old footer taught "Tab/I close | Q drop": Tab is the stance and Q the teleport.
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Inventory");
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(f);
                Assert.IsFalse(src.Contains("Tab/I"), Path.GetFileName(f) + " names Tab as a key.");
                Assert.IsFalse(src.Contains("Q drop"), Path.GetFileName(f) + " names Q as the drop key.");
            }
        }

        [Test]
        public void TheStyleShipsTheTrayIcon_AndTheThemeLoads()
        {
            var style = Resources.Load<InventoryHudStyle>(InventoryHudStyle.ResourcePath);
            Assert.IsNotNull(style, "Resources/UI/InventoryHudStyle.asset is missing.");
            Assert.IsNotNull(style.trayIcon, "The tray button's sprite must ship in the style asset.");
            Assert.IsNotNull(Resources.Load<HudTheme>(HudTheme.ResourcePath), "Resources/UI/HudTheme.asset is missing.");
        }

        // ── Placement ────────────────────────────────────────────────────

        [Test]
        public void TheWindowOpensBesideTheMinimapColumn_NotOverIt()
        {
            var tl = _ui.DefaultTopLeft();
            float right = tl.x + _ui.SizeTexels.x * _ui.PixelScale;
            float root = _ui.Panel.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
            float columnLeft = Screen.width - (HudLayout.ScreenMargin + HudLayout.TopRightColumnWidth) * root;
            Assert.LessOrEqual(right, columnLeft + 0.5f, "The window's right edge must stay left of the minimap column.");
            float bandLeft = Screen.width - HudLayout.GameWindowRightInset * root;
            Assert.LessOrEqual(right, bandLeft + 0.5f, "...and left of the music panel, the widest instrument on the right.");
        }

        // ── Paper doll ───────────────────────────────────────────────────

        [Test]
        public void ThePaperDoll_HasOneSlotPerKind_EachWithASilhouette()
        {
            var kinds = new HashSet<EquipmentSlotKind>();
            for (int i = 0; i < EquipmentLayout.Count; i++)
            {
                var v = _ui.EquipView(i);
                Assert.IsNotNull(v);
                Assert.IsTrue(v.IsEquipment);
                Assert.IsTrue(kinds.Add(v.Kind), "Duplicate slot kind " + v.Kind);
                Assert.IsTrue(v.SilhouetteShown, v.Kind + " shows no silhouette while empty.");
            }
        }

        [Test]
        public void RightClick_WearsTheItem_InItsOwnSlot()
        {
            var helm = Item("helm", EquipSlot.Helmet);
            _inventory.SetSlot(0, helm, 1);
            _ui.ActivateSlot(0);
            Assert.AreEqual(helm, _inventory.EquipmentSlots[(int)EquipmentSlotKind.Head].Item);
            Assert.IsFalse(_ui.EquipView((int)EquipmentSlotKind.Head).SilhouetteShown);
            Assert.IsTrue(_ui.EquipView((int)EquipmentSlotKind.Head).IconShown ||
                          _ui.EquipView((int)EquipmentSlotKind.Head).Item == helm);
        }

        // ── Rarity ───────────────────────────────────────────────────────

        [Test]
        public void Rarity_IsShapeAsWellAsColour()
        {
            var rarities = new[] { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic };
            int[] expected = { 0, 1, 2, 4 };
            for (int i = 0; i < rarities.Length; i++)
                _inventory.SetSlot(i, Item("r" + i, rarity: rarities[i]), 1);
            for (int i = 0; i < rarities.Length; i++)
                Assert.AreEqual(expected[i], _ui.BagView(i).CornersShown, rarities[i] + " corners");
        }

        // ── Pickups by difference ────────────────────────────────────────

        [Test]
        public void APickup_IsAnnounced_WithMotesAndANewMark()
        {
            var gem = Item("gem", rarity: ItemRarity.Epic, stackable: true, maxStack: 10);
            _inventory.AddItem(gem, 3);
            Assert.Greater(_ui.Motes.Alive, 0, "A find throws light.");
            Assert.IsTrue(_ui.BagView(0).IsNew, "A find is marked new until looked at.");
            Assert.IsTrue(_ui.BagView(0).Flashing);
        }

        [Test]
        public void TheWindowsOwnMoves_AreNeverAnnouncedAsFinds()
        {
            var ore = Item("ore", stackable: true, maxStack: 20);
            _inventory.SetSlot(5, ore, 4);
            var sword = Item("sword", EquipSlot.Weapon);
            _inventory.SetSlot(3, sword, 1);
            Run(2f);
            LookAtEverything();
            Assert.IsFalse(AnyNew(), "Fixture: hovering a slot clears its new mark.");

            _ui.ActivateSlot(3);                       // equip
            Assert.IsFalse(AnyNew(), "Equipping is not a find.");
        }

        [Test]
        public void ASaveRestore_IsNotAFind()
        {
            // GameStateRestorer calls Initialize and then SetSlot per saved slot, all in one frame.
            _inventory.Initialize(Inventory.DefaultBagCapacity);
            _inventory.SetSlot(0, Item("a"), 1);
            _inventory.SetSlot(1, Item("b"), 1);
            Assert.IsFalse(AnyNew(), "Loading a save must not mark the whole bag new.");
            Assert.AreEqual(0, _ui.Motes.Alive);
        }

        private void LookAtEverything()
        {
            for (int i = 0; i < _ui.BagViewCount; i++)
            {
                _ui.OnSlotPointer(i, true);
                _ui.OnSlotPointer(i, false);
            }
        }

        private bool AnyNew()
        {
            for (int i = 0; i < _ui.BagViewCount; i++) if (_ui.BagView(i).IsNew) return true;
            return false;
        }

        [Test]
        public void MotesDie_AndNeverExceedTheBudget()
        {
            for (int i = 0; i < 10; i++)
                _inventory.AddItem(Item("leg" + i, rarity: ItemRarity.Legendary), 1);
            Assert.LessOrEqual(_ui.Motes.Alive, _ui.Motes.Capacity);
            Run(3f);
            Assert.AreEqual(0, _ui.Motes.Alive, "Every mote answers an event and dies; nothing emits at rest.");
        }

        // ── Refusals ─────────────────────────────────────────────────────

        [Test]
        public void EveryRefusalLine_IsSpellableInTheBitmapFace()
        {
            var patterns = HudPixelFont.Glyphs(HudFontFace.Small);
            var sword = Item("sword", EquipSlot.Weapon);
            foreach (EquipRefusal r in System.Enum.GetValues(typeof(EquipRefusal)))
            {
                string line = InventoryUI.RefusalLine(r, sword);
                Assert.IsTrue(HudPixelFont.CanSpell(line, patterns), r + ": '" + line + "' has characters the face cannot draw.");
            }
            foreach (EquipmentSlotKind k in System.Enum.GetValues(typeof(EquipmentSlotKind)))
            {
                var it = Item("x" + k, KindToEquipSlot(k));
                string line = InventoryUI.RefusalLine(EquipRefusal.WrongSlot, it);
                Assert.IsTrue(HudPixelFont.CanSpell(line, patterns), line);
            }
        }

        private static EquipSlot KindToEquipSlot(EquipmentSlotKind k)
        {
            switch (k)
            {
                case EquipmentSlotKind.Head: return EquipSlot.Helmet;
                case EquipmentSlotKind.Amulet: return EquipSlot.Amulet;
                case EquipmentSlotKind.Weapon: return EquipSlot.Weapon;
                case EquipmentSlotKind.Offhand: return EquipSlot.Shield;
                case EquipmentSlotKind.Chest: return EquipSlot.Chest;
                case EquipmentSlotKind.Ring: return EquipSlot.Ring;
                case EquipmentSlotKind.Boots: return EquipSlot.Boots;
                default: return EquipSlot.Trinket;
            }
        }

        [Test]
        public void TakingOffIntoAFullBag_IsRefused_NotSilent()
        {
            for (int i = 0; i < _inventory.Capacity; i++) _inventory.SetSlot(i, Item("rock" + i), 1);
            var helm = Item("helm", EquipSlot.Helmet);
            _inventory.SetEquipmentSlot((int)EquipmentSlotKind.Head, helm, 1);
            _ui.ActivateSlot(_inventory.Capacity + (int)EquipmentSlotKind.Head);
            Assert.AreEqual(helm, _inventory.EquipmentSlots[(int)EquipmentSlotKind.Head].Item);
            Assert.IsTrue(_ui.EquipView((int)EquipmentSlotKind.Head).Flashing, "A refusal flashes the slot.");
        }

        // ── The card ─────────────────────────────────────────────────────

        [Test]
        public void TheCard_ComparesAgainstWhatIsWorn()
        {
            var worn = Item("worn", EquipSlot.Weapon);
            worn.damage = 8;
            var better = Item("better", EquipSlot.Weapon);
            better.damage = 15;
            _inventory.SetEquipmentSlot((int)EquipmentSlotKind.Weapon, worn, 1);
            _inventory.SetSlot(0, better, 1);

            var card = _ui.Card;
            card.Build(better, 1, false, worn, 5, HudTheme.Active);
            StringAssert.Contains("Frente a worn", card.Body);
            StringAssert.Contains("+7", card.Body, "The difference is the number that matters.");
            StringAssert.Contains("Clic derecho: equipar", card.Body);
            Assert.Greater(card.Height, 20);
        }

        // ── Dropping valuables ───────────────────────────────────────────

        [Test]
        public void DroppingAnEpic_AsksFirst_AndNoKeepsIt()
        {
            var epic = Item("crown", rarity: ItemRarity.Epic);
            _inventory.SetSlot(0, epic, 1);
            _ui.RequestWorldDrop(0, null, 0);
            Assert.IsTrue(_ui.Confirm.Visible, "An Epic item is not thrown away without asking.");
            Assert.AreEqual(epic, _inventory.Slots[0].Item);
            _ui.Confirm.Resolve(false);
            Assert.AreEqual(epic, _inventory.Slots[0].Item, "Answering no keeps it.");

            _ui.RequestWorldDrop(0, null, 0);
            _ui.Confirm.Resolve(true);
            Assert.IsTrue(_inventory.Slots[0].IsEmpty, "Answering yes drops it.");
        }

        [Test]
        public void DroppingACommon_DoesNotAsk()
        {
            var pebble = Item("pebble");
            _inventory.SetSlot(0, pebble, 1);
            _ui.RequestWorldDrop(0, null, 0);
            Assert.IsFalse(_ui.Confirm.Visible, "Confirming every pebble teaches the player to click through.");
            Assert.IsTrue(_inventory.Slots[0].IsEmpty);
        }

        [Test]
        public void TheCard_IsOpaque()
        {
            foreach (var img in _ui.Card.Root.GetComponentsInChildren<Image>(true))
                if (img.name == "Card") Assert.IsNotNull(img.sprite, "The card is a drawn, opaque recess.");
        }
    }
}
