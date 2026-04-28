using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.Spells.UI;
using Valkur.Gameplay.UI;

namespace Valkur.Tests.EditMode.Game.UI
{
    public class SpellDragAndDropTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();
            SpellDragContext.End();
            ClearSingletonInstance<SpellBarHUD>();
        }

        [TearDown]
        public void TearDown()
        {
            SpellDragContext.End();
            EntityRegistry.Clear();
            ClearSingletonInstance<SpellBarHUD>();

            foreach (var obj in _objects)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }

            _objects.Clear();
        }

        [Test]
        public void HudDropFlow_AssignsFromPickerAndSwapsBetweenSlots()
        {
            var fireball = CreateSpell("fireball");
            var frostbolt = CreateSpell("frostbolt");
            var hud = CreateHud(out var caster, out var canvas);

            var slot0 = GetDropZone(hud, 0);
            var slot1 = GetDropZone(hud, 1);

            SpellDragContext.Begin(fireball, null, SpellDragOrigin.Picker, -1, canvas, new Vector2(32f, 32f));
            slot0.OnDrop(null);

            Assert.AreSame(fireball, caster.GetSpellAtSlot(0));

            caster.SetSpell(1, frostbolt);

            SpellDragContext.Begin(fireball, null, SpellDragOrigin.HudSlot, 0, canvas, new Vector2(48f, 48f));
            slot1.OnDrop(null);

            Assert.AreSame(frostbolt, caster.GetSpellAtSlot(0));
            Assert.AreSame(fireball, caster.GetSpellAtSlot(1));
        }

        [Test]
        public void DragHover_HighlightsTargetSlotInYellow()
        {
            var spell = CreateSpell("arcane_blast");
            var hud = CreateHud(out _, out var canvas);
            var zone = GetDropZone(hud, 0);
            var image = zone.GetComponent<Image>();

            Assert.IsNotNull(image);
            var original = image.color;

            SpellDragContext.Begin(spell, null, SpellDragOrigin.Picker, -1, canvas, new Vector2(16f, 16f));
            zone.OnPointerEnter(null);

            Assert.AreNotEqual(original, image.color);
            Assert.AreEqual(new Color(1f, 0.83f, 0.18f, 0.95f), image.color);

            zone.OnPointerExit(null);
            Assert.AreEqual(original, image.color);
        }

        [Test]
        public void DragContext_CreatesYellowGhostPreview()
        {
            var spell = CreateSpell("meteor");
            var hud = CreateHud(out _, out var canvas);

            SpellDragContext.Begin(spell, null, SpellDragOrigin.Picker, -1, canvas, new Vector2(100f, 120f));

            Assert.IsTrue(SpellDragContext.IsDragging);
            Assert.IsNotNull(SpellDragContext.GhostObject);
            Assert.IsTrue(SpellDragContext.GhostObject.activeSelf);
            Assert.IsNotNull(SpellDragContext.GhostObject.GetComponent<Outline>());
            Assert.IsNotNull(SpellDragContext.GhostObject.GetComponent<Shadow>());
        }

        private SpellBarHUD CreateHud(out SpellCaster caster, out Canvas canvas)
        {
            var player = new GameObject("Player");
            _objects.Add(player);

            caster = player.AddComponent<SpellCaster>();
            EntityRegistry.RegisterPlayer(player);

            var hudGo = new GameObject("SpellBarHUD");
            _objects.Add(hudGo);
            var hud = hudGo.AddComponent<SpellBarHUD>();
            InvokeIfPresent(hud, "OnSingletonAwake");
            InvokeIfPresent(hud, "ResolvePlayer");
            InvokeIfPresent(hud, "Populate");

            canvas = hudGo.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "SpellBarHUD should build its own canvas in OnSingletonAwake.");
            return hud;
        }

        private DropZoneSpellSlot GetDropZone(SpellBarHUD hud, int slotIndex)
        {
            foreach (var zone in hud.GetComponentsInChildren<DropZoneSpellSlot>(true))
            {
                if (zone.name == $"Slot_{slotIndex}")
                {
                    InvokeIfPresent(zone, "Awake");
                    return zone;
                }
            }

            Assert.Fail($"Drop zone for slot {slotIndex} was not found.");
            return null;
        }

        private SpellDefinition CreateSpell(string key)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = key;
            spell.displayName = key;
            spell.type = SpellType.Projectile;
            spell.cooldownDuration = 1f;
            _objects.Add(spell);
            return spell;
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, null);
                    return;
                }

                type = type.BaseType;
            }
        }

        private static void InvokeIfPresent(object target, string methodName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(target, null);
                    return;
                }

                type = type.BaseType;
            }
        }
    }
}
