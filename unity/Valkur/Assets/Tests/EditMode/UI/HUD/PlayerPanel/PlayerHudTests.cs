using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.PlayerPanel
{
    /// <summary>
    /// The bottom-left player panel, driven frame by frame through <see cref="PlayerHUD.Tick"/>
    /// (Edit Mode has no player loop) and read back through what the player would SEE.
    ///
    /// <para>Layout itself cannot be measured here — uGUI performs no layout in Edit Mode — so
    /// the geometry tests assert the AUTHORED rects, which is all the panel uses: it places every
    /// child by hand, bottom-left, on whole texels, precisely so that the authored rect IS the
    /// drawn one. A rendered frame is the check for the look; these pin the behaviour.</para>
    /// </summary>
    [TestFixture]
    public class PlayerHudTests
    {
        private GameObject _canvasGo;
        private GameObject _player;
        private Health _health;
        private Mana _mana;
        private Experience _xp;
        private SpellCaster _caster;
        private PlayerHUD _hud;
        private readonly List<Object> _spells = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _canvasGo = new GameObject("HUDCanvas", typeof(RectTransform));
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _player = new GameObject("Player");
            _health = _player.AddComponent<Health>();
            _health.Initialize(100);
            _mana = _player.AddComponent<Mana>();
            _mana.Initialize(50, 50, 0f);
            _xp = _player.AddComponent<Experience>();
            _caster = _player.AddComponent<SpellCaster>();

            _hud = PlayerHUD.Create(canvas, _health, _mana);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hud != null) Object.DestroyImmediate(_hud.gameObject);
            if (_player != null) Object.DestroyImmediate(_player);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            foreach (var s in _spells) if (s != null) Object.DestroyImmediate(s);
            _spells.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private void Run(float seconds, float dt = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += dt) _hud.Tick(dt);
        }

        private SpellDefinition Spell(string key, float mana, float cooldown)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.displayName = key;
            s.manaCost = mana;
            s.cooldownDuration = cooldown;
            s.prepareDuration = 0.5f;          // so TryCastByKey never executes anything here
            _spells.Add(s);
            return s;
        }

        // -- Pixel grid -----------------------------------------------------------------

        [TestCase(1600, 800, 2)]
        [TestCase(1366, 683, 2)]
        [TestCase(1920, 1080, 3)]
        [TestCase(2560, 1280, 3)]
        [TestCase(2560, 1440, 3)]
        [TestCase(3840, 2160, 5)]
        [TestCase(800, 400, 1)]
        public void PixelScale_IsAWholeNumber_ThatGrowsWithTheScreen(int w, int h, int expected)
        {
            var style = ScriptableObject.CreateInstance<PlayerHudStyle>();
            try { Assert.AreEqual(expected, style.HudPixelScaleFor(w, h)); }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void PixelScale_NeverFallsUnderTheFloor_ForADegenerateScreen()
        {
            Assert.AreEqual(1, PlayerHudStyle.HudPixelScale(0, 0, new Vector2(1600, 800), 2f, 1));
            Assert.AreEqual(2, PlayerHudStyle.HudPixelScale(10, 10, new Vector2(1600, 800), 2f, 2));
        }

        [Test]
        public void ThePanel_IsExactly_ItsTexelsTimesTheScale_InCanvasUnits()
        {
            var canvas = _canvasGo.GetComponent<Canvas>();
            float root = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            var size = _hud.SizeTexels;
            float per = _hud.PixelScale / root;
            Assert.AreEqual(size.x * per, _hud.Panel.sizeDelta.x, 0.001f);
            Assert.AreEqual(size.y * per, _hud.Panel.sizeDelta.y, 0.001f);

            // The corner sits on a whole SCREEN pixel, or every texel inside is off the grid.
            float cornerPx = _hud.Panel.anchoredPosition.x * root;
            Assert.AreEqual(Mathf.Round(cornerPx), cornerPx, 0.001f);
        }

        [Test]
        public void EveryRectInThePixelSpace_SitsOnWholeTexels()
        {
            var pixels = _hud.Panel.Find("Pixels");
            Assert.IsNotNull(pixels);
            int checkedRects = 0;
            foreach (var rt in pixels.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == pixels) continue;
                // Stretched children (the mote layer) are exempt: they carry no position of their own.
                if (rt.anchorMin != rt.anchorMax) continue;
                var p = rt.anchoredPosition;
                var s = rt.sizeDelta;
                Assert.AreEqual(Mathf.Round(p.x), p.x, 0.0001f, rt.name + " x off the texel grid");
                Assert.AreEqual(Mathf.Round(p.y), p.y, 0.0001f, rt.name + " y off the texel grid");
                Assert.AreEqual(Mathf.Round(s.x), s.x, 0.0001f, rt.name + " width off the texel grid");
                Assert.AreEqual(Mathf.Round(s.y), s.y, 0.0001f, rt.name + " height off the texel grid");
                Assert.AreEqual(Vector2.zero, rt.pivot, rt.name + " must pivot bottom-left");
                checkedRects++;
            }
            Assert.Greater(checkedRects, 40, "Vacuous: the walk found almost nothing to check.");
        }

        [Test]
        public void NoNineSlice_IsSmallerThanItsBorders()
        {
            // uGUI squashes a sliced sprite whose borders add up to more than its rect, and every
            // border row then lands on half a texel. Measured on the 5-texel XP line with a 3+3
            // frame: the only row of the whole panel off the grid in a live capture.
            int checkedImages = 0;
            foreach (var img in _hud.GetComponentsInChildren<Image>(true))
            {
                if (img.type != Image.Type.Sliced || img.sprite == null) continue;
                var b = img.sprite.border;          // left, bottom, right, top
                var size = img.rectTransform.sizeDelta;
                Assert.GreaterOrEqual(size.x, b.x + b.z, img.name + " is narrower than its borders");
                Assert.GreaterOrEqual(size.y, b.y + b.w, img.name + " is shorter than its borders");
                checkedImages++;
            }
            Assert.Greater(checkedImages, 5, "Vacuous: no sliced images were found.");
        }

        [Test]
        public void OnlyTheControlsCatchTheMouse()
        {
            // The panel sits over the play area, and PlayerController refuses to cast while the
            // pointer is over interactive UI. Only the portrait (double-click: sheet), the three
            // slot frames (tooltip) and the XP line (tooltip) may be raycast targets.
            int targets = 0;
            foreach (var g in _hud.GetComponentsInChildren<Graphic>(true))
                if (g.raycastTarget) targets++;
            Assert.AreEqual(1 + 3 + 1, targets);
        }

        // -- Health -----------------------------------------------------------------------

        [Test]
        public void Boot_ShowsTheStartingHealth_WithoutAnimatingUpFromNothing()
        {
            Assert.AreEqual("100/100", _hud.HealthBar.Label);
            Assert.AreEqual(1f, _hud.HealthBar.Shown, 0.0001f);
            Assert.IsFalse(_hud.HealthBar.ChipActive);
        }

        [Test]
        public void ABlow_DropsTheFillAtOnce_AndLeavesAChipThatDrains()
        {
            _health.TakeDamage(30);
            Assert.AreEqual("70/100", _hud.HealthBar.Label);
            Assert.AreEqual(0.7f, _hud.HealthBar.Shown, 0.0001f, "A loss drops the fill immediately.");
            Assert.IsTrue(_hud.HealthBar.ChipActive, "The lost span must stay visible as a chip.");
            Assert.IsTrue(_hud.HealthBar.Flashing, "A blow flashes the bar.");

            Run(0.1f);
            Assert.IsTrue(_hud.HealthBar.ChipActive, "The chip HOLDS before it drains.");
            // The model saying "chip" is not the player seeing one: the first build kept the chip
            // image disabled because its width had not changed, and only a live capture showed it.
            Assert.IsTrue(_hud.HealthBar.ChipDrawn, "The chip must be DRAWN, not only modelled.");
            Run(2f);
            Assert.IsFalse(_hud.HealthBar.ChipActive, "…and it has drained after two seconds.");
        }

        [Test]
        public void AHeal_IsNotABlow()
        {
            _health.TakeDamage(50);
            Run(2f);
            _health.Heal(30);
            Assert.IsTrue(_hud.HealthBar.GainActive, "A heal lights the incoming span.");
            Assert.IsFalse(_hud.HealthBar.ChipActive, "A heal leaves no chip.");
            Assert.Less(_hud.HealthBar.Shown, _hud.HealthBar.Target, "The fill GROWS into a heal.");
            Assert.AreNotEqual("80/100", _hud.HealthBar.Label, "The number counts up WITH the fill.");
            Run(1f);
            Assert.AreEqual(0.8f, _hud.HealthBar.Shown, 0.01f);
            Assert.AreEqual("80/100", _hud.HealthBar.Label, "…and lands on the real value.");
        }

        [Test]
        public void ABlow_ThrowsMotes_WithinTheBudget_AndTheyAllDie()
        {
            _health.TakeDamage(40);
            Assert.Greater(_hud.Motes.Alive, 0, "A blow is an event; it gets motes.");
            Assert.LessOrEqual(_hud.Motes.Alive, PlayerHudStyle.Active.motesOnHit);
            Run(3f);
            Assert.AreEqual(0, _hud.Motes.Alive, "Nothing on the panel emits at rest.");
        }

        [Test]
        public void AtRest_TheMoteLayerIsEmpty()
        {
            Run(2f);
            Assert.AreEqual(0, _hud.Motes.Alive);
        }

        [Test]
        public void LowHealth_TurnsTheFill_BeatsTheGlow_AndRaisesTheScreenEdge()
        {
            var healthy = _hud.HealthBar.FillColour;
            _health.TakeDamage(85);
            Run(1.5f);
            Assert.IsTrue(_hud.HealthBar.HeartbeatActive);
            Assert.AreNotEqual(healthy, _hud.HealthBar.FillColour, "Low health must change the fill.");
            Assert.Greater(_hud.DangerEdge.Alpha, 0f, "The screen edge warns at low health.");
            Assert.Less(_hud.Portrait.Saturation, 1f, "Colour drains out of the portrait.");
        }

        [Test]
        public void Death_StopsTheHeartbeat_AndGreysThePortrait()
        {
            _health.TakeDamage(1000);
            Run(1.5f);
            Assert.IsFalse(_hud.HealthBar.HeartbeatActive, "A spirit has no pulse to warn about.");
            Assert.AreEqual(0f, _hud.Portrait.Saturation, 0.001f);
        }

        // -- Mana -------------------------------------------------------------------------

        [Test]
        public void ABarWithNoMaximum_PrintsNothing()
        {
            var other = new GameObject("NoMana");
            try
            {
                var h = other.AddComponent<Health>();
                h.Initialize(10);
                _hud.Bind(h);
                Assert.AreEqual("", _hud.ManaBar.Label, "No mana component: say nothing, not 0/0.");
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Mana_ShowsCurrentOverMax_AndSpendingChips()
        {
            Assert.AreEqual("50/50", _hud.ManaBar.Label);
            _mana.TryConsume(20);
            Assert.AreEqual("30/50", _hud.ManaBar.Label);
            Assert.IsTrue(_hud.ManaBar.ChipActive, "A spend leaves a chip, like a blow.");
        }

        [Test]
        public void ARefusedCast_FlashesTheManaBar_AndTheSlot()
        {
            _caster.RegisterSpell(PlayerController.SecondarySpellKey, Spell(PlayerController.SecondarySpellKey, 80f, 1f));
            Run(0.05f);
            bool cast = _caster.TryCastByKey(PlayerController.SecondarySpellKey, Vector2.right);
            Assert.IsFalse(cast, "Sanity: 50 mana cannot pay 80.");
            Assert.IsTrue(_hud.ManaBar.Flashing, "A refusal must SAY why the click did nothing.");
            Assert.AreEqual(HudSlotState.ManaShort, _hud.Slot(1).State);
        }

        // -- Experience ------------------------------------------------------------------------

        [Test]
        public void ExperienceGained_SweepsAGlint_AndFloatsTheAmount()
        {
            _xp.AddXp(5);
            Assert.IsTrue(_hud.XpBar.Glinting);
            Assert.Greater(_hud.FloatingText.Active, 0);
            Run(2f);
            Assert.AreEqual(0, _hud.FloatingText.Active);
        }

        [Test]
        public void ALevelUp_PulsesTheMedallion_AndPrintsTheNewLevel()
        {
            int before = _xp.Level;
            _xp.AddXp(_xp.XpRequiredForLevel(before + 1) - _xp.TotalXp);
            Assert.AreEqual(before + 1, _xp.Level, "Sanity: the award crossed a level.");
            Assert.AreEqual((before + 1).ToString(), _hud.Medallion.Label);
            Assert.IsTrue(_hud.Medallion.Pulsing);
            Assert.Greater(_hud.Motes.Alive, 10, "A level is the biggest event the panel has.");
            Run(3f);
            Assert.IsFalse(_hud.Medallion.Pulsing);
            Assert.AreEqual(_xp.NormalizedProgress, _hud.XpBar.Shown, 0.02f,
                "After the celebration the line shows the NEW level's progress.");
        }

        // -- Slots ------------------------------------------------------------------------------

        [Test]
        public void TheSlots_ShowWhatTheMouseButtonsCast()
        {
            Run(0.05f);
            Assert.AreEqual("fireball", _hud.Slot(0).SpellKey);
            Assert.AreEqual(PlayerController.SecondarySpellKey, _hud.Slot(1).SpellKey);
            Assert.AreEqual(PlayerController.MiddleSpellKey, _hud.Slot(2).SpellKey);
        }

        [Test]
        public void ASpellTheCharacterKnows_IsReady_AndCoolsDownWithTheBookClock()
        {
            _caster.RegisterSpell(PlayerController.SecondarySpellKey, Spell(PlayerController.SecondarySpellKey, 5f, 6f));
            Run(0.05f);
            Assert.AreEqual(HudSlotState.Ready, _hud.Slot(1).State);

            // The book keeps its own cooldown clock per key; the old ring read the SLOT clock,
            // which a mouse cast never sets.
            var field = typeof(SpellCaster).GetField("_spellBookCooldowns", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "SpellCaster renamed its book cooldowns; repoint this test.");
            var book = (Dictionary<string, float>)field.GetValue(_caster);
            book[PlayerController.SecondarySpellKey] = 3.2f;
            Run(0.02f);
            Assert.AreEqual(HudSlotState.Cooldown, _hud.Slot(1).State);
            Assert.AreEqual("4", _hud.Slot(1).SecondsLabel, "Whole seconds, rounded UP.");
            Assert.That(_hud.Slot(1).Cooldown01, Is.EqualTo(3.2f / 6f).Within(0.02f));
        }

        [Test]
        public void ASlotComingOffCooldown_FlashesAndThrowsMotes()
        {
            _caster.RegisterSpell(PlayerController.SecondarySpellKey, Spell(PlayerController.SecondarySpellKey, 5f, 6f));
            var field = typeof(SpellCaster).GetField("_spellBookCooldowns", BindingFlags.NonPublic | BindingFlags.Instance);
            var book = (Dictionary<string, float>)field.GetValue(_caster);
            book[PlayerController.SecondarySpellKey] = 1f;
            Run(0.05f);
            Assert.AreEqual(0, _hud.Motes.Alive);
            book[PlayerController.SecondarySpellKey] = 0f;
            Run(0.02f);
            Assert.Greater(_hud.Motes.Alive, 0, "Coming back is an event.");
        }

        [Test]
        public void AShortCooldownEnding_IsNotCelebrated()
        {
            // Holding left click re-casts a half-second fireball twice a second; a ring of sparks
            // on each would turn an event into a fountain.
            _caster.RegisterSpell(PlayerController.SecondarySpellKey, Spell(PlayerController.SecondarySpellKey, 5f, 0.5f));
            var field = typeof(SpellCaster).GetField("_spellBookCooldowns", BindingFlags.NonPublic | BindingFlags.Instance);
            var book = (Dictionary<string, float>)field.GetValue(_caster);
            book[PlayerController.SecondarySpellKey] = 0.4f;
            Run(0.05f);
            book[PlayerController.SecondarySpellKey] = 0f;
            Run(0.02f);
            Assert.AreEqual(0, _hud.Motes.Alive);
        }

        [Test]
        public void ASpellNotInTheBook_IsNeverShownAsReady()
        {
            Run(0.05f);
            var state = _hud.Slot(2).State;
            Assert.That(state == HudSlotState.Locked || state == HudSlotState.Empty,
                "A spell the character cannot cast must not look castable (was " + state + ").");
        }

        [Test]
        public void HoveringASlot_ShowsItsCard_AndLeavingHidesIt()
        {
            _caster.RegisterSpell(PlayerController.SecondarySpellKey, Spell(PlayerController.SecondarySpellKey, 5f, 6f));
            Run(0.05f);
            var hover = _hud.Slot(1).Root.GetComponent<HudSlotHover>();
            Assert.IsNotNull(hover);
            hover.Entered();
            Assert.IsTrue(_hud.Tooltip.Visible);
            Assert.AreEqual(PlayerController.SecondarySpellKey, _hud.Tooltip.Title);
            Assert.GreaterOrEqual(_hud.Tooltip.Root.anchoredPosition.y, _hud.SizeTexels.y,
                "The card opens ABOVE the panel; over it, it hides the bars being read.");
            hover.Exited();
            Assert.IsFalse(_hud.Tooltip.Visible);
        }

        // -- Art and data ------------------------------------------------------------------------

        [Test]
        public void TheShippedStyle_CarriesTheHudFxShader()
        {
            // A shader found only by Shader.Find is stripped from a player build; the style
            // asset's reference is what ships it. Without it the motes stop being additive.
            var style = Resources.Load<PlayerHudStyle>(PlayerHudStyle.ResourcePath);
            Assert.IsNotNull(style, "Resources/UI/PlayerHudStyle.asset is missing.");
            Assert.IsNotNull(style.hudFxShader, "The style must reference Valkur/UI/HudFx.");
            Assert.AreEqual("Valkur/UI/HudFx", style.hudFxShader.name);
        }

        [TestCase(HudFontFace.Small)]
        [TestCase(HudFontFace.Large)]
        public void EveryGlyph_IsARectangleOfTheFacesHeight(HudFontFace face)
        {
            int h = HudPixelFont.HeightOf(face);
            foreach (var kv in HudPixelFont.Glyphs(face))
            {
                // RECTANGULAR is the invariant the atlas packing depends on, and it holds for
                // every glyph without exception: rows of differing width really do break it.
                int w = kv.Value[0].Length;
                foreach (var row in kv.Value)
                    Assert.AreEqual(w, row.Length, "'" + kv.Key + "' is ragged: every row must be as wide.");

                // HEIGHT is not that invariant, and asserting it was is what made a correct
                // feature fail. HudPixelText seats every quad on the same baseline and takes
                // its height from the glyph, so a Spanish capital grows UPWARD by
                // HudPixelFont.AccentRows to carry its accent — a five-row face has no room for
                // one inside the height of a capital. The set allowed to do it is closed, so
                // "a glyph may be taller" cannot become "any glyph may be any height".
                int allowed = HudPixelFont.ClaimsAccentRows(kv.Key) ? h + HudPixelFont.AccentRows : h;
                Assert.AreEqual(allowed, kv.Value.Length,
                    "'" + kv.Key + "' has the wrong number of rows. Only the Spanish set may be " +
                    "taller, and only by HudPixelFont.AccentRows — do NOT fix a red here by " +
                    "trimming rows, which removes the accent or the crossbar.");
            }
        }

        [Test]
        public void TheSmallFace_SpellsTheSpanishTheHudActuallyDraws()
        {
            // Counting one accented glyph is not counting the set. The words below are drawn
            // TODAY by the grimoire's role filter and its card, upper-cased into this face, so
            // a missing glyph is a chip with a hole in it rather than a hypothetical.
            var small = HudPixelFont.Glyphs(HudFontFace.Small);
            foreach (var word in new[] { "DAÑO", "PROTECCIÓN", "CURACIÓN", "MOVILIDAD",
                                         "INVOCACIÓN", "UTILIDAD", "NIVEL", "TE QUEDAN" })
                Assert.IsTrue(HudPixelFont.CanSpell(word, small), word);
        }

        [Test]
        public void EveryGlyphAllowedToBeTaller_IsActuallyInTheFace()
        {
            // The other direction of the same rule: a character declared as accent-claiming
            // but absent from the font is a permission for something that does not exist, and
            // it would let the closed set drift away from the glyphs without anything failing.
            var small = HudPixelFont.Glyphs(HudFontFace.Small);
            foreach (var c in "ÁÉÍÓÚÜÑ¿¡")
            {
                Assert.IsTrue(HudPixelFont.ClaimsAccentRows(c), "'" + c + "' should be declared");
                Assert.IsTrue(small.ContainsKey(c), "'" + c + "' is declared but not drawn");
                Assert.AreEqual(HudPixelFont.HeightOf(HudFontFace.Small) + HudPixelFont.AccentRows,
                                small[c].Length, "'" + c + "'");
            }
        }

        [Test]
        public void TheLargeFace_SpellsEveryHealthNumber()
        {
            var large = HudPixelFont.Glyphs(HudFontFace.Large);
            Assert.IsTrue(HudPixelFont.CanSpell("0123456789/", large));
            Assert.IsTrue(HudPixelFont.CanSpell("1250/1250", large));
        }

        [Test]
        public void TheAtlas_IsPointFiltered_AndItsFramesAreNineSliced()
        {
            var art = HudArt.Get();
            Assert.AreEqual(FilterMode.Point, art.Atlas.filterMode, "Pixel art under bilinear is soft.");
            foreach (var s in new[] { art.BarFrame, art.Slot, art.PortraitFrame, art.StatusTile })
                Assert.Greater(s.border.x, 0f, s.name + " stretches its own corners without a border.");
            Assert.AreEqual(100f, art.White.pixelsPerUnit, "One atlas pixel must be one panel texel.");
        }

        [Test]
        public void TheStatusGlyphs_AreTheWorldBarsOwnTable()
        {
            var art = HudArt.Get();
            Assert.AreEqual(Valkur.Gameplay.Combat.StatusGlyphs.Count, art.StatusGlyph.Length,
                "The corner and the head must draw the same set of status glyphs.");
            foreach (var g in art.StatusGlyph) Assert.IsNotNull(g);
        }

        // -- Rebinding ---------------------------------------------------------------------------

        [Test]
        public void Rebinding_ToAnotherPlayer_LeavesTheOldOneUnheard()
        {
            var other = new GameObject("Other");
            try
            {
                var h2 = other.AddComponent<Health>();
                h2.Initialize(40);
                _hud.Bind(h2);
                Assert.AreEqual("40/40", _hud.HealthBar.Label);
                _health.TakeDamage(10);
                Assert.AreEqual("40/40", _hud.HealthBar.Label, "The old player's events must be unhooked.");
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }
    }
}
