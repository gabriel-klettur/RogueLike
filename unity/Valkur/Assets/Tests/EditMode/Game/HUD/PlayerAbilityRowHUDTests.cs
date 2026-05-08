using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="PlayerAbilityRowHUD"/>: Bind stores references, Refresh is
    /// safe with no SpellCaster in scope, icons are coloured correctly per filled/
    /// empty spell slot, and only slots within the icons array bounds are touched.
    ///
    /// Update() is not executed by Unity's NUnit runner in EditMode; tests call
    /// the internal <c>Refresh()</c> seam added to <see cref="PlayerAbilityRowHUD"/>
    /// for exactly this purpose.
    /// </summary>
    [TestFixture]
    public class PlayerAbilityRowHUDTests
    {
        private static readonly Color GrayColor = new Color(0.30f, 0.30f, 0.35f, 0.45f);

        private GameObject _hudGo;
        private PlayerAbilityRowHUD _hud;

        private GameObject _playerGo;
        private SpellCaster _caster;

        // Icon Image objects
        private Image[] _icons;
        private CooldownRing[] _rings;

        [SetUp]
        public void SetUp()
        {
            // Image/CooldownRing on bare GameObjects emit renderer warnings in EditMode.
            LogAssert.ignoreFailingMessages = true;

            EntityRegistry.Clear();

            _hudGo = new GameObject("PlayerAbilityRowHUD");
            _hud = _hudGo.AddComponent<PlayerAbilityRowHUD>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);

            // Destroy any icon/ring GOs created in individual tests.
            if (_icons != null)
            {
                foreach (var icon in _icons)
                    if (icon != null) Object.DestroyImmediate(icon.gameObject);
            }
            if (_rings != null)
            {
                foreach (var ring in _rings)
                    if (ring != null) Object.DestroyImmediate(ring.gameObject);
            }

            EntityRegistry.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private (Image[], CooldownRing[]) BuildSlots(int count)
        {
            _icons = new Image[count];
            _rings = new CooldownRing[count];

            for (int i = 0; i < count; i++)
            {
                var iconGo = new GameObject($"Icon{i}", typeof(RectTransform));
                _icons[i] = iconGo.AddComponent<Image>();

                var ringGo = new GameObject($"Ring{i}", typeof(RectTransform));
                _rings[i] = ringGo.AddComponent<CooldownRing>();
            }

            return (_icons, _rings);
        }

        private GameObject BuildPlayer(int spellSlotCount = 3)
        {
            _playerGo = new GameObject("Player");
            _caster = _playerGo.AddComponent<SpellCaster>();
            // SpellCaster serializes spellSlots as a 4-element array by default;
            // that is fine — we only fill the slots we care about.
            EntityRegistry.RegisterPlayer(_playerGo);
            return _playerGo;
        }

        private SpellDefinition MakeSpell(string key)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = key;
            spell.displayName = key;
            spell.type = SpellType.Projectile;
            spell.sprite = null; // no sprite needed for icon colour tests
            return spell;
        }

        // ── Tests ────────────────────────────────────────────────────────────────

        [Test]
        public void Bind_StoresReferences_SmokeTest()
        {
            var (icons, rings) = BuildSlots(3);
            BuildPlayer();

            // Bind then trigger one Refresh — must not throw NRE.
            Assert.DoesNotThrow(
                () =>
                {
                    _hud.Bind(_playerGo, icons, rings);
                    _hud.Refresh();
                },
                "Bind + Refresh on a valid player must not throw.");
        }

        [Test]
        public void Refresh_NoCasterReachable_DoesNotThrow_AndLeavesIconsUntouched()
        {
            var (icons, rings) = BuildSlots(3);

            // Paint the icons a sentinel colour so we can detect unwanted writes.
            foreach (var icon in icons) icon.color = Color.magenta;

            // Do NOT register any player in EntityRegistry.
            _hud.Bind(null, icons, rings);

            Assert.DoesNotThrow(() => _hud.Refresh(),
                "Refresh with no SpellCaster in scope must not throw.");

            foreach (var icon in icons)
                Assert.AreEqual(Color.magenta, icon.color,
                    "Icons must be left untouched when no caster is reachable.");
        }

        [Test]
        public void Refresh_SpellAtSlot0Only_WhiteFor0_GrayFor1And2()
        {
            var (icons, rings) = BuildSlots(3);
            BuildPlayer();

            var spell0 = MakeSpell("fireball");
            try
            {
                _caster.SetSpell(0, spell0);
                // slots 1 and 2 remain null (default)

                _hud.Bind(_playerGo, icons, rings);
                _hud.Refresh();

                Assert.AreEqual(Color.white, icons[0].color,
                    "Slot 0 has a spell — icon must be white.");
                Assert.AreEqual(GrayColor, icons[1].color,
                    "Slot 1 is empty — icon must be gray.");
                Assert.AreEqual(GrayColor, icons[2].color,
                    "Slot 2 is empty — icon must be gray.");
            }
            finally
            {
                Object.DestroyImmediate(spell0);
            }
        }

        [Test]
        public void Refresh_OnlyTouchesSlots_WithinIconsArrayLength()
        {
            // 2-slot icons array — even if the SpellCaster has 4 slots, only 0..1
            // should ever be read/written.
            var (icons, rings) = BuildSlots(2);
            BuildPlayer();

            var spell0 = MakeSpell("slash");
            var spell3 = MakeSpell("aura");
            try
            {
                _caster.SetSpell(0, spell0);
                _caster.SetSpell(3, spell3); // outside icons range

                _hud.Bind(_playerGo, icons, rings);

                // Must not throw (no index-out-of-range) and must only set the
                // two icons we handed it.
                Assert.DoesNotThrow(() => _hud.Refresh());

                Assert.AreEqual(Color.white, icons[0].color,
                    "Slot 0 spell present — icon[0] must be white.");
                Assert.AreEqual(GrayColor, icons[1].color,
                    "Slot 1 spell absent — icon[1] must be gray (slot 3 must not bleed).");
            }
            finally
            {
                Object.DestroyImmediate(spell0);
                Object.DestroyImmediate(spell3);
            }
        }
    }
}
