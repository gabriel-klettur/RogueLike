using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Crafting;
using Valkur.Gameplay.HUD;
using Valkur.Gameplay.Inventory;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// What each Peace verb does, when it has anything to act on, and when what it opens is open.
    /// Each one goes to the SAME entry point its key or its own button already uses, so the bar
    /// is a second way in and never a second implementation.
    /// </summary>
    public sealed partial class SpellBarHUD
    {
        private MinimapHUD _minimap;
        private float _minimapLookLeft;

        private bool VerbExists(string id)
        {
            switch (id)
            {
                case "interact": return _interaction != null;
                case "inventory": return InventoryUI.HasInstance;
                case "map": return Minimap() != null;
                case "crafting": return CraftingPanelUI.HasInstance;
                case "quests": return QuestLogHUD.HasInstance;
                case "talents":
                case "grimoire": return true;
                default: return false;
            }
        }

        /// <summary>The same suppression the stance key and the interact key honour: a chat or
        /// the console is up, or an editor owns the world.</summary>
        private static bool VerbSuppressed()
        {
            if (InputBlocker.IsGameplayBlocked) return true;
            return GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive;
        }

        private HudSlotVerb MakeVerb(SpellBarEntry entry)
        {
            if (entry.Kind == SpellBarEntryKind.Stance) return MakeStanceVerb();
            if (!SpellBarModel.TryGetVerb(entry.Key, out var spec)) return null;

            var verb = new HudSlotVerb
            {
                Id = spec.Id,
                Title = spec.Title,
                Glyph = _barArt.Glyph(spec.Glyph),
                Tint = TintFor(spec.Glyph),
                IdleStrength = _style.idleVerbStrength,
            };

            switch (spec.Id)
            {
                case "interact":
                    verb.IsAvailable = InteractAvailable;
                    verb.Detail = InteractDetail;
                    verb.Invoke = Interact;
                    break;
                case "inventory":
                    verb.IsActive = () => InventoryUI.HasInstance && InventoryUI.Instance.IsVisible;
                    verb.Detail = () => "La mochila y el equipo";
                    verb.Invoke = () =>
                    {
                        if (InventoryUI.HasInstance) InventoryUI.Instance.SetVisible(!InventoryUI.Instance.IsVisible);
                    };
                    break;
                case "map":
                    verb.IsActive = () => Minimap() != null && Minimap().WorldMapOpen;
                    verb.Detail = () => "Lo explorado y tu marca";
                    verb.Invoke = () => Minimap()?.ToggleWorldMap();
                    break;
                case "crafting":
                    verb.IsActive = () => CraftingPanelUI.HasInstance && CraftingPanelUI.Instance.IsVisible;
                    verb.Detail = () => "Recetas y oficios";
                    verb.Invoke = () =>
                    {
                        if (CraftingPanelUI.HasInstance) CraftingPanelUI.Instance.Toggle();
                    };
                    break;
                case "quests":
                    verb.IsActive = () => QuestLogHUD.HasInstance && QuestLogHUD.Instance.IsWindowVisible;
                    verb.Detail = () => "Los encargos en curso";
                    verb.Invoke = () =>
                    {
                        if (QuestLogHUD.HasInstance) QuestLogHUD.Instance.ToggleClosed();
                    };
                    break;
                case "talents":
                    verb.IsActive = () => SheetShowing(CharacterSheetController.TabSkills);
                    verb.Detail = () => "Gasta los puntos de habilidad";
                    verb.Invoke = () => ToggleSheet(CharacterSheetController.TabSkills);
                    // Levelling up granted a point and the game said so nowhere: the number lived
                    // inside the panel this slot opens, so the only way to learn you had one was
                    // to go and look. The badge is the notice.
                    verb.Badge = UnspentSkillPoints;
                    break;
                case "grimoire":
                    verb.IsActive = () => SheetShowing(CharacterSheetController.TabGrimoire);
                    verb.Detail = () => "Aprende hechizos nuevos";
                    verb.Invoke = () => ToggleSheet(CharacterSheetController.TabGrimoire);
                    break;
            }
            return verb;
        }

        /// <summary>
        /// The switch always shows where it LEADS, in that posture's colour: a leaf on the War
        /// face, crossed blades on the Peace face. The chip in the top-left corner reports where
        /// the player IS; a switch that showed the same thing would read as a second indicator.
        /// </summary>
        private HudSlotVerb MakeStanceVerb()
        {
            bool toPeace = _face == Stance.War;
            return new HudSlotVerb
            {
                Id = SpellBarModel.StanceKey,
                Title = toPeace ? "Pasar a la paz" : "Pasar a la guerra",
                Glyph = _barArt.Glyph(toPeace ? SpellBarGlyph.ToPeace : SpellBarGlyph.ToWar),
                Tint = _style.AccentFor(toPeace ? Stance.Peace : Stance.War),
                Detail = () => toPeace
                    ? "Sin combate: habla, comercia y trabaja"
                    : "Vuelven los hechizos y el combate",
                Invoke = PlayerStance.Toggle,
            };
        }

        private Color TintFor(SpellBarGlyph glyph)
        {
            switch (glyph)
            {
                case SpellBarGlyph.Interact: return _style.interactTint;
                case SpellBarGlyph.Inventory: return _style.inventoryTint;
                case SpellBarGlyph.Map: return _style.mapTint;
                case SpellBarGlyph.Crafting: return _style.craftingTint;
                case SpellBarGlyph.Quests: return _style.questsTint;
                case SpellBarGlyph.Talents: return _style.talentsTint;
                case SpellBarGlyph.Grimoire: return _style.grimoireTint;
                default: return _hud.gold;
            }
        }

        // -- Interact -------------------------------------------------------------------------

        private bool InteractAvailable()
        {
            var target = _interaction != null ? _interaction.CurrentTarget : null;
            return target != null && _player != null && target.DescribePrompt(_player).IsActionable;
        }

        private string InteractDetail()
        {
            var target = _interaction != null ? _interaction.CurrentTarget : null;
            if (target == null || _player == null) return "No hay nada al alcance";
            var prompt = target.DescribePrompt(_player);
            return string.IsNullOrEmpty(prompt.Verb) ? "Al alcance" : prompt.Verb;
        }

        /// <summary>Through the interaction controller, which owns reachability and the session:
        /// the same path a double click on an NPC takes.</summary>
        private void Interact()
        {
            if (_interaction == null) return;
            var target = _interaction.CurrentTarget;
            if (target != null) _interaction.TryInteractWith(target);
        }

        // -- The character sheet ----------------------------------------------------------------

        /// <summary>
        /// Skill points the player has not spent, read from the model the talents board reads —
        /// never from the board itself, which is normally not built at all while the bar is up.
        /// </summary>
        private static int UnspentSkillPoints()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return 0;
            var skills = player.GetComponent<Valkur.Gameplay.LearnedSkills>();
            return skills != null ? skills.AvailablePoints : 0;
        }

        private static bool SheetShowing(int tab)
        {
            if (!CharacterSheetController.HasInstance) return false;
            var sheet = CharacterSheetController.Instance;
            return sheet.IsOpen && sheet.ActiveTab == tab;
        }

        private static void ToggleSheet(int tab)
        {
            var sheet = AbilityRowDoubleClick.ResolveSheet();
            if (sheet == null) return;
            if (sheet.IsOpen && sheet.ActiveTab == tab) sheet.Close();
            else sheet.Open(tab);
        }

        // -- The world map ------------------------------------------------------------------------

        /// <summary>The minimap owns the world map. Looked up once and cached; while it is missing
        /// the look is retried at most once a second, never every frame.</summary>
        private MinimapHUD Minimap()
        {
            if (_minimap != null) return _minimap;
            if (_minimapLookLeft > 0f && Application.isPlaying && Time.unscaledTime < _minimapLookLeft) return null;
            _minimapLookLeft = Application.isPlaying ? Time.unscaledTime + 1f : 0f;
            _minimap = FindObjectOfType<MinimapHUD>(true);
            return _minimap;
        }
    }
}
