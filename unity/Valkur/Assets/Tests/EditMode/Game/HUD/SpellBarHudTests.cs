using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The action bar, built and driven through <see cref="SpellBarHUD.Tick"/> (Edit Mode has no
    /// player loop), read back through what the player would see.
    ///
    /// <para>Layout cannot be measured here — uGUI performs no layout in Edit Mode — so geometry
    /// is asserted on the AUTHORED rects, which is all the bar uses: every child is placed by hand
    /// on whole texels. A rendered frame is the check for the look.</para>
    /// </summary>
    [TestFixture]
    public class SpellBarHudTests
    {
        private GameObject _canvasGo;
        private GameObject _player;
        private SpellCaster _caster;
        private SpellBarHUD _bar;
        private readonly List<Object> _spells = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerStance.ResetForTests();
            InputContextPolicy.ResetForTests();
            _canvasGo = new GameObject("HUDCanvas", typeof(RectTransform));
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _player = new GameObject("Player");
            _player.AddComponent<Health>().Initialize(100);
            _player.AddComponent<Mana>().Initialize(50, 50, 0f);
            _caster = _player.AddComponent<SpellCaster>();
            _caster.RegisterSpell("darkball", Spell("darkball"));
            _caster.RegisterSpell("glacial_step", Spell("glacial_step"));   // a bare key: T

            _bar = SpellBarHUD.Create(canvas, _player);
        }

        [TearDown]
        public void TearDown()
        {
            if (_bar != null) Object.DestroyImmediate(_bar.gameObject);
            if (_player != null) Object.DestroyImmediate(_player);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            foreach (var s in _spells) if (s != null) Object.DestroyImmediate(s);
            _spells.Clear();
            PlayerStance.ResetForTests();
            InputContextPolicy.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        private SpellDefinition Spell(string key)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.displayName = key;
            s.manaCost = 5f;
            s.cooldownDuration = 3f;
            s.prepareDuration = 0.5f;
            _spells.Add(s);
            return s;
        }

        private int Run(float seconds, float dt = 1f / 60f)
        {
            int maxMotes = 0;
            for (float t = 0f; t < seconds; t += dt)
            {
                _bar.Tick(dt);
                maxMotes = Mathf.Max(maxMotes, _bar.Motes.Alive);
            }
            return maxMotes;
        }

        private List<SpellBarEntry> Entries()
        {
            var list = new List<SpellBarEntry>();
            for (int i = 0; i < _bar.SlotCount; i++) list.Add(_bar.Entry(i));
            return list;
        }

        [Test]
        public void WarFace_ShowsTheKnownSpells_WithTheirKeys_ThenTheSwitch()
        {
            _bar.Tick(1f / 60f);   // a slot derives its state on its first tick
            var e = Entries();
            Assert.AreEqual(Stance.War, _bar.Face);
            Assert.AreEqual(new[] { "darkball", "glacial_step", SpellBarModel.StanceKey }, e.Select(x => x.Key).ToArray());
            Assert.AreEqual(HudSlotState.Ready, _bar.Slot(0).State);
            Assert.AreEqual("darkball", _bar.Slot(0).SpellKey);
        }

        [Test]
        public void ChangingPosture_TurnsTheBarOver_ToTheOtherFace_WithMotes()
        {
            Run(0.1f);
            PlayerStance.Set(Stance.Peace);
            _bar.Tick(1f / 60f);
            Assert.IsTrue(_bar.Flipping, "The flip starts on the frame the posture changes.");
            Assert.AreEqual(Stance.War, _bar.Face, "The old face stays up while it turns away.");

            int motes = Run(1.5f);
            Assert.IsFalse(_bar.Flipping);
            Assert.AreEqual(Stance.Peace, _bar.Face);
            Assert.IsFalse(Entries().Any(x => x.Kind == SpellBarEntryKind.Spell), "No spell on the Peace face.");
            Assert.AreEqual(SpellBarEntryKind.Stance, Entries().Last().Kind);
            Assert.Greater(motes, 0, "The posture change is the bar's one loud event: it throws motes.");
            Run(2f);
            Assert.AreEqual(0, _bar.Motes.Alive, "Motes answer an event and die; nothing emits at rest.");
        }

        [Test]
        public void TheSwitch_PointsAtTheOtherPosture()
        {
            var sw = _bar.Slot(_bar.SlotCount - 1);
            Assert.AreEqual(SpellBarModel.StanceKey, sw.Verb.Id);
            StringAssert.Contains("paz", sw.Verb.Title.ToLowerInvariant(), "On the War face the switch leads to Peace.");
            Assert.AreEqual(SpellBarStyle.Active.peaceAccent, sw.Verb.Tint);
        }

        [Test]
        public void ALearnedSpell_JoinsTheBar_AndIsCelebrated()
        {
            Run(0.3f);
            Assert.AreEqual(0, _bar.Motes.Alive);
            _caster.RegisterSpell("iceball", Spell("iceball"));
            int motes = Run(0.4f);
            CollectionAssert.Contains(Entries().Select(x => x.Key).ToList(), "iceball");
            Assert.Greater(motes, 0, "A spell arriving on the bar is an event.");
        }

        [Test]
        public void EveryRect_SitsOnWholeTexels()
        {
            PlayerStance.Set(Stance.Peace);
            Run(1.5f);
            var pixels = _bar.transform.GetChild(0);
            foreach (var rt in pixels.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == pixels) continue;
                var p = rt.anchoredPosition;
                var s = rt.sizeDelta;
                Assert.AreEqual(Mathf.Round(p.x), p.x, 1e-4f, rt.name + " x");
                Assert.AreEqual(Mathf.Round(p.y), p.y, 1e-4f, rt.name + " y");
                Assert.AreEqual(Mathf.Round(s.x), s.x, 1e-4f, rt.name + " w");
                Assert.AreEqual(Mathf.Round(s.y), s.y, 1e-4f, rt.name + " h");
            }
            Assert.GreaterOrEqual(_bar.SizeTexels.y, SpellBarStyle.Active.SlotTexels + SpellBarStyle.Active.paddingTexels * 2);
        }

        [Test]
        public void MoreSpellsThanARow_WrapIntoASecondRow_OnTheGrid_WithTheSwitchStillOnTheBottomRow()
        {
            foreach (var key in new[] { "iceball", "lightball", "charged_bolt", "lightning", "boomerang",
                                        "seeking_shard", "scatter_volley", "laser_beam_red", "lightning_beam", "teleport" })
                _caster.RegisterSpell(key, Spell(key));
            Run(0.4f);

            var style = SpellBarStyle.Active;
            int spells = Entries().Count(x => x.Kind == SpellBarEntryKind.Spell);
            Assert.AreEqual(12, spells);
            int rowStep = style.SlotTexels + style.slotGapTexels;
            Assert.GreaterOrEqual(_bar.SizeTexels.y, style.paddingTexels + 2 * style.SlotTexels + style.slotGapTexels,
                "Twelve spells at ten a row need two rows.");

            // The switch closes the BOTTOM row: it is the one slot every face must keep in reach.
            int stance = -1, eleventh = -1, spellSeen = 0;
            for (int i = 0; i < _bar.SlotCount; i++)
            {
                if (_bar.Entry(i).Kind == SpellBarEntryKind.Stance) { stance = i; continue; }
                if (spellSeen++ == 10) eleventh = i;
            }
            Assert.GreaterOrEqual(stance, 0);
            var firstBox = (RectTransform)_bar.Slot(0).Root.parent;
            var stanceBox = (RectTransform)_bar.Slot(stance).Root.parent;
            Assert.AreEqual(firstBox.anchoredPosition.y, stanceBox.anchoredPosition.y, 1e-4f);
            var upper = (RectTransform)_bar.Slot(eleventh).Root.parent;
            Assert.AreEqual(firstBox.anchoredPosition.y + rowStep, upper.anchoredPosition.y, 1e-4f,
                "The eleventh spell opens the second row, one slot and one gap up.");
        }

        [Test]
        public void AVerbSlot_ReadsIdleReadyAndActive()
        {
            var go = new GameObject("VerbProbe", typeof(RectTransform));
            try
            {
                bool available = false, active = false;
                var slot = new HudAbilitySlot(go.transform, HudArt.Get(), 0, 0, 0, 22, () => null, () => null, null);
                slot.SetVerb(new HudSlotVerb
                {
                    Id = "probe",
                    Glyph = SpellBarArt.Get().Glyph(SpellBarGlyph.Map),
                    IsAvailable = () => available,
                    IsActive = () => active,
                });
                var style = PlayerHudStyle.Active;
                slot.Tick(0.016f, null, null, style, 2);
                Assert.AreEqual(HudSlotState.Idle, slot.State);
                available = true;
                slot.Tick(0.016f, null, null, style, 2);
                Assert.AreEqual(HudSlotState.Ready, slot.State);
                active = true;
                slot.Tick(0.016f, null, null, style, 2);
                Assert.AreEqual(HudSlotState.Active, slot.State);
                Assert.IsNull(slot.Spell, "A verb slot resolves no spell.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // -- Source guards ---------------------------------------------------------------

        private static string ScriptsRoot => Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string StripComments(string src) =>
            Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");

        [Test]
        public void TheBar_NeverCastsOnItsOwn()
        {
            var dir = Path.Combine(ScriptsRoot, "UI", "HUD", "SpellBar");
            var files = Directory.GetFiles(dir, "*.cs");
            Assert.IsNotEmpty(files);
            foreach (var f in files)
            {
                var src = StripComments(File.ReadAllText(f));
                StringAssert.DoesNotContain("TryCastByKey(", src, Path.GetFileName(f) +
                    ": the old bar cast through SpellCaster directly and fired damage spells in Peace.");
                StringAssert.DoesNotContain(".TryCast(", src, Path.GetFileName(f));
                StringAssert.DoesNotContain("PlayerStance.Set(", src, Path.GetFileName(f) +
                    ": the bar may TOGGLE the posture like the key does, never force one.");
            }
        }

        [Test]
        public void ACastFromTheBar_GoesThroughEveryGateTheKeyDoes()
        {
            var src = StripComments(File.ReadAllText(Path.Combine(ScriptsRoot, "Gameplay", "Player", "PlayerController.Movement.cs")));
            int start = src.IndexOf("public bool TryCastFromHud(", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, "PlayerController.TryCastFromHud is the one way the bar casts.");
            int end = src.IndexOf("public void SetMoveSpeed(", start, System.StringComparison.Ordinal);
            var body = src.Substring(start, end - start);
            foreach (var gate in new[]
            {
                "PlayerStance.IsPeace", "InputContextPolicy.IsLive", "IsGameplayInputSuspended()",
                "IsPlayerCombatSuspended()", "InputBlocker.IsGameplayBlocked", "IsSpirit", "IsStunned",
            })
                StringAssert.Contains(gate, body, "TryCastFromHud is missing the " + gate + " gate.");
            Assert.Less(body.IndexOf("PlayerStance.IsPeace", System.StringComparison.Ordinal),
                        body.IndexOf("TryCastByKey(", System.StringComparison.Ordinal),
                        "The posture is checked BEFORE anything is cast.");
        }
    }
}
