using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>The visible state of one ability slot, derived every frame. For the tests.</summary>
    public enum HudSlotState
    {
        Empty = 0,
        Locked = 1,
        Ready = 2,
        Cooldown = 3,
        ManaShort = 4,
        /// <summary>A verb with nothing to act on right now (no one in reach to talk to).</summary>
        Idle = 5,
        /// <summary>A verb whose panel is open.</summary>
        Active = 6,
    }

    /// <summary>
    /// One of the panel's three mouse slots: the spell a mouse button casts, its cooldown, and
    /// whether it can be cast at all.
    ///
    /// <para><b>The slot shows what the BUTTON does.</b> The row it replaces read SpellCaster
    /// slots 0-2 under the labels "1", "2", "3" — but slot 0 is what LEFT CLICK casts, slots 1-2
    /// are empty for the player, and the number keys cast the 24-slot spell book, not those
    /// slots. So the only spell on the row was labelled with a key that did not cast it, its
    /// cooldown ring read a clock the left click never set (the book keeps its own), and a spell
    /// with no icon rendered as a bare white square. Each slot now names its spell key through
    /// <see cref="PlayerController"/>'s constants, reads the BOOK cooldown for it, and shows the
    /// button from the action's live binding — rebind right click and the glyph moves.</para>
    ///
    /// <para><b>Five states, each with its own look</b> so none of them has to be read from a
    /// number: ready (full colour), cooling (a dark clock wipe plus whole seconds), short of mana
    /// (icon washed blue), not learned (grey icon under a padlock), empty.</para>
    ///
    /// <para><b>The icon is minified on the GPU</b> to the slot's own pixel size
    /// (<see cref="HudTextureBaker.Icon"/>); the source art is 1024x1024 with no mipmaps.</para>
    /// </summary>
    public sealed class HudAbilitySlot
    {
        private const int Inset = 2;

        public RectTransform Root { get; }

        private readonly RawImage _icon;
        private readonly Image _cooldown;
        private readonly Image _flash;
        private readonly Image _glow;
        private readonly Image _lock;
        private readonly Image _mouseGlyph;
        private readonly HudPixelText _seconds;
        private readonly HudPixelText _keyLabel;
        private readonly HudArt _art;
        private readonly int _size;
        private readonly Func<string> _spellKey;
        private readonly Func<InputAction> _action;

        private SpellDefinition _spell;
        private string _key;
        private Sprite _iconSprite;
        private int _iconPixels;
        private float _cooldown01;
        private float _cooldownTotal;
        private float _readyFlash;
        private float _refusedLeft;
        private bool _pressed;
        private string _boundPath;
        private HudSlotState _state = HudSlotState.Empty;
        private HudSlotVerb _verb;
        private Image _glyph;
        private Vector2Int _glyphAt;
        private bool _verbSeen;
        private bool _verbWasAvailable;

        /// <summary>The state the slot is showing.</summary>
        public HudSlotState State => _state;

        /// <summary>The spell key the slot is reporting on.</summary>
        public string SpellKey => _key;

        /// <summary>The spell it resolved, when there is one.</summary>
        public SpellDefinition Spell => _spell;

        /// <summary>Cooldown remaining, 0..1.</summary>
        public float Cooldown01 => _cooldown01;

        /// <summary>The whole seconds printed over the icon, empty when none.</summary>
        public string SecondsLabel => _seconds.Text;

        /// <summary>True while the button behind the slot is held.</summary>
        public bool Pressed => _pressed;

        /// <summary>The non-spell action this slot shows, when it shows one.</summary>
        public HudSlotVerb Verb => _verb;

        /// <summary>The edge of the slot in texels.</summary>
        public int Size => _size;

        /// <summary>Raised when the cooldown runs out, with the slot's panel-space centre.</summary>
        public event Action<HudAbilitySlot> BecameReady;

        public HudAbilitySlot(Transform parent, HudArt art, int index, int x, int y, int size,
                              Func<string> spellKey, Func<InputAction> action, Material additive)
        {
            _art = art;
            _size = size;
            _spellKey = spellKey;
            _action = action;
            Root = HudRect.Make("Slot_" + index, parent, x, y, size, size);

            // The frame is the raycast target: hovering it opens the tooltip, and the row's
            // double-click opens the character sheet.
            var frame = HudRect.MakeImage("Frame", Root, art.Slot, 0, 0, size, size, Image.Type.Sliced);
            frame.raycastTarget = true;

            int inner = size - Inset * 2;
            var iconRt = HudRect.Make("Icon", Root, Inset, Inset, inner, inner);
            _icon = iconRt.gameObject.AddComponent<RawImage>();
            _icon.raycastTarget = false;
            _icon.enabled = false;

            _cooldown = HudRect.MakeImage("Cooldown", Root, art.White, Inset, Inset, inner, inner, Image.Type.Filled);
            _cooldown.fillMethod = Image.FillMethod.Radial360;
            _cooldown.fillOrigin = (int)Image.Origin360.Top;
            _cooldown.fillClockwise = false;
            _cooldown.fillAmount = 0f;
            _cooldown.enabled = false;

            _flash = HudRect.MakeImage("Flash", Root, art.White, Inset, Inset, inner, inner);
            _flash.material = additive;
            _flash.color = Color.clear;
            _flash.enabled = false;

            _glow = HudRect.MakeImage("Glow", Root, art.SlotGlow, 0, 0, size, size, Image.Type.Sliced);
            _glow.material = additive;
            _glow.color = Color.clear;
            _glow.enabled = false;

            _lock = HudRect.MakeImage("Lock", Root, art.Lock, (size - 7) / 2, (size - 8) / 2, 7, 8);
            _lock.enabled = false;

            _seconds = HudPixelText.Create(Root, "Seconds", art, HudFontFace.Small, HudTextAlign.Centre,
                                           Inset, Inset, inner, inner);

            // The button that casts it: a badge on the bottom-right corner, hanging one texel over
            // the frame so it reads as attached to the slot rather than painted on the spell.
            _mouseGlyph = HudRect.MakeImage("Button", Root, art.MouseLeft, size - 6, -1, 7, 9);
            _mouseGlyph.enabled = false;
            _keyLabel = HudPixelText.Create(Root, "Key", art, HudFontFace.Small, HudTextAlign.Right,
                                            1, 0, size - 2, 7);
        }

        /// <summary>Panel-space centre of the slot, given the slot row's origin.</summary>
        public Vector2 Centre => Root.anchoredPosition + new Vector2(_size * 0.5f, _size * 0.5f);

        /// <summary>A refused cast on this slot's spell: the slot flashes the mana colour.</summary>
        public void Refused(PlayerHudStyle style)
        {
            _refusedLeft = style.refusedFlashSeconds;
        }

        /// <summary>The ready flash, on demand: a verb that was just used, a spell just learned.</summary>
        public void Flash(PlayerHudStyle style)
        {
            _readyFlash = style.readyFlashSeconds;
        }

        /// <summary>
        /// Turns the slot into a VERB slot: no spell, no cooldown, a pixel glyph at its native
        /// size in the middle. Called once, straight after construction.
        /// </summary>
        public void SetVerb(HudSlotVerb verb)
        {
            _verb = verb;
            if (verb == null) return;
            _icon.enabled = false;
            if (_glyph == null)
            {
                _glyph = HudRect.MakeImage("Glyph", Root, verb.Glyph, 0, 0, 1, 1);
                // Above the icon and the cooldown, below the flash, glow, text and badge.
                _glyph.transform.SetSiblingIndex(_icon.transform.GetSiblingIndex() + 1);
            }
            _glyph.sprite = verb.Glyph;
            int w = verb.Glyph != null ? Mathf.RoundToInt(verb.Glyph.rect.width) : 0;
            int h = verb.Glyph != null ? Mathf.RoundToInt(verb.Glyph.rect.height) : 0;
            _glyphAt = new Vector2Int((_size - w) / 2, (_size - h) / 2);
            HudRect.Place(_glyph.rectTransform, _glyphAt.x, _glyphAt.y, w, h);
            _glyph.enabled = verb.Glyph != null;
            _verbSeen = false;
        }

        /// <summary>The colour this slot's flashes take: the verb's tint or the spell's swatch.</summary>
        public Color AccentColour(PlayerHudStyle style) => _verb != null ? _verb.Tint : SpellColour(style);

        public void Tick(float dt, SpellCaster caster, Mana mana, PlayerHudStyle style, int pixelScale)
        {
            if (_verb != null)
            {
                TickVerb(dt, style);
                return;
            }

            string key = _spellKey != null ? _spellKey() : null;
            if (key != _key)
            {
                _key = key;
                _spell = ResolveSpell(caster, key);
                _iconSprite = null;
            }
            else if (_spell == null && !string.IsNullOrEmpty(key))
            {
                _spell = ResolveSpell(caster, key);
            }

            UpdateBinding();
            UpdateIcon(pixelScale);

            bool known = caster != null && !string.IsNullOrEmpty(key) && caster.KnowsSpell(key);
            float cd = 0f;
            if (known && _spell != null)
            {
                float total = caster.ResolveCooldown(_spell);
                float remaining = caster.GetBookCooldownRemaining(key);
                cd = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
                if (remaining > 0f) _cooldownTotal = total;
                SetSeconds(remaining);
            }
            else SetSeconds(0f);

            bool wasCooling = _cooldown01 > 0.001f;
            _cooldown01 = cd;
            if (wasCooling && cd <= 0.001f && known && _cooldownTotal >= style.readyFlashMinCooldown)
            {
                _readyFlash = style.readyFlashSeconds;
                BecameReady?.Invoke(this);
            }

            bool shortOfMana = known && _spell != null && mana != null &&
                               caster.ResolveManaCost(_spell) > mana.CurrentMana;

            _state = _spell == null ? HudSlotState.Empty
                   : !known ? HudSlotState.Locked
                   : cd > 0.001f ? HudSlotState.Cooldown
                   : shortOfMana ? HudSlotState.ManaShort
                   : HudSlotState.Ready;

            var action = _action != null ? _action() : null;
            _pressed = known && action != null && InputBindingResolver.IsPressed(action);

            Draw(dt, style, shortOfMana);
        }

        private void Draw(float dt, PlayerHudStyle style, bool shortOfMana)
        {
            // Icon tint: the state in one glance.
            Color tint = Color.white;
            switch (_state)
            {
                case HudSlotState.Locked: tint = style.lockedIcon; break;
                case HudSlotState.ManaShort: tint = style.manaShortIcon; break;
                case HudSlotState.Cooldown: tint = shortOfMana ? style.manaShortIcon : Color.white; break;
            }
            if (_icon.color != tint) _icon.color = tint;

            bool lockOn = _state == HudSlotState.Locked;
            if (_lock.enabled != lockOn) _lock.enabled = lockOn;

            bool cooling = _state == HudSlotState.Cooldown;
            if (_cooldown.enabled != cooling) _cooldown.enabled = cooling;
            if (cooling)
            {
                // Whole steps of 1/64 so the wipe does not rebuild its mesh for invisible changes.
                float q = Mathf.Ceil(_cooldown01 * 64f) / 64f;
                if (!Mathf.Approximately(_cooldown.fillAmount, q)) _cooldown.fillAmount = q;
                if (_cooldown.color != style.cooldownShade) _cooldown.color = style.cooldownShade;
            }

            // A held button presses the icon down one texel — the slot answers the hand.
            int iconY = Inset - (_pressed ? 1 : 0);
            var ip = _icon.rectTransform.anchoredPosition;
            if ((int)ip.y != iconY) _icon.rectTransform.anchoredPosition = new Vector2(Inset, iconY);

            DrawFeedback(dt, style, SpellColour(style), 0f);
        }

        /// <summary>
        /// The flash, the ring and the press, shared by spells and verbs. <paramref name="steady"/>
        /// is a ring that holds rather than fades: a verb whose panel is open.
        /// </summary>
        private void DrawFeedback(float dt, PlayerHudStyle style, Color accent, float steady)
        {
            // Ready flash: a white burst over the icon and a ring round the frame in the spell's
            // own colour, fading over readyFlashSeconds.
            if (_readyFlash > 0f) _readyFlash = Mathf.Max(0f, _readyFlash - dt);
            if (_refusedLeft > 0f) _refusedLeft = Mathf.Max(0f, _refusedLeft - dt);

            float flashA = _readyFlash > 0f ? _readyFlash / Mathf.Max(0.01f, style.readyFlashSeconds) : 0f;
            bool flashOn = flashA > 0.001f;
            if (_flash.enabled != flashOn) _flash.enabled = flashOn;
            if (flashOn) _flash.color = new Color(1f, 1f, 1f, 0.75f * flashA * flashA);

            Color glow = steady > 0f ? WithAlpha(accent, steady) : Color.clear;
            if (_readyFlash > 0f) glow = WithAlpha(accent, Mathf.Max(flashA, steady));
            if (_refusedLeft > 0f)
                glow = WithAlpha(style.mana, _refusedLeft / Mathf.Max(0.01f, style.refusedFlashSeconds));
            if (_pressed) glow = Color.Lerp(glow, new Color(1f, 1f, 1f, 0.45f), 0.6f);
            bool glowOn = glow.a > 0.001f;
            if (_glow.enabled != glowOn) _glow.enabled = glowOn;
            if (glowOn && _glow.color != glow) _glow.color = glow;
        }

        // -- Verb ---------------------------------------------------------------------

        private void TickVerb(float dt, PlayerHudStyle style)
        {
            UpdateBinding();
            if (_icon.enabled) _icon.enabled = false;
            if (_cooldown.enabled) _cooldown.enabled = false;
            if (_lock.enabled) _lock.enabled = false;
            _seconds.SetText("");
            _cooldown01 = 0f;

            bool available = _verb.Available;
            bool active = available && _verb.Active;
            // Something to act on just arrived: a tree in reach, a vendor in range. That is an
            // EVENT, so it flashes once; being in reach afterwards is a state and does not.
            if (_verbSeen && available && !_verbWasAvailable)
                _readyFlash = Mathf.Max(_readyFlash, style.readyFlashSeconds * 0.6f);
            _verbSeen = true;
            _verbWasAvailable = available;

            _state = active ? HudSlotState.Active : available ? HudSlotState.Ready : HudSlotState.Idle;

            var action = _action != null ? _action() : null;
            _pressed = available && action != null && InputBindingResolver.IsPressed(action);

            if (_glyph != null)
            {
                Color c;
                if (_state == HudSlotState.Idle)
                {
                    var grey = new Color(0.36f, 0.37f, 0.42f, 1f);
                    c = Color.Lerp(grey, _verb.Tint, Mathf.Clamp01(_verb.IdleStrength));
                    c.a = 0.8f;
                }
                else if (_state == HudSlotState.Active)
                {
                    c = Color.Lerp(_verb.Tint, Color.white, 0.22f);
                    c.a = 1f;
                }
                else
                {
                    c = _verb.Tint;
                    c.a = 1f;
                }
                if (_glyph.color != c) _glyph.color = c;

                // A held key presses the glyph down one texel, exactly as it does a spell icon.
                int y = _glyphAt.y - (_pressed ? 1 : 0);
                var p = _glyph.rectTransform.anchoredPosition;
                if ((int)p.y != y) _glyph.rectTransform.anchoredPosition = new Vector2(_glyphAt.x, y);
            }

            DrawFeedback(dt, style, _verb.Tint, _state == HudSlotState.Active ? 0.55f : 0f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            return c;
        }

        /// <summary>The spell's authored swatch, or the panel's gold for the unauthored sentinel.</summary>
        public Color SpellColour(PlayerHudStyle style)
        {
            if (_spell == null || KiPalette.IsUnauthored(_spell.particleColor)) return style.gold;
            var c = _spell.particleColor;
            c.a = 1f;
            return c;
        }

        private void SetSeconds(float remaining)
        {
            // Whole seconds, rounded UP, from one second out: under a second the wipe says enough,
            // and a number that reads "0" while the spell is still cooling is a lie.
            string s = remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : "";
            _seconds.SetText(s);
        }

        private void UpdateIcon(int pixelScale)
        {
            var sprite = _spell != null ? (_spell.iconSprite != null ? _spell.iconSprite : _spell.sprite) : null;
            int px = (_size - Inset * 2) * Mathf.Max(1, pixelScale);
            if (sprite == _iconSprite && px == _iconPixels && (_icon.enabled || sprite == null)) return;
            _iconSprite = sprite;
            _iconPixels = px;

            if (sprite == null)
            {
                _icon.enabled = false;
                _icon.texture = null;
                return;
            }

            var baked = HudTextureBaker.Icon(sprite, px);
            if (baked != null)
            {
                _icon.texture = baked;
                _icon.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            else
            {
                // No GPU (batch mode): draw the sprite's own region of its texture.
                _icon.texture = sprite.texture;
                var t = sprite.textureRect;
                var tex = sprite.texture;
                _icon.uvRect = tex != null
                    ? new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height)
                    : new Rect(0f, 0f, 1f, 1f);
            }
            _icon.enabled = true;
        }

        private void UpdateBinding()
        {
            var action = _action != null ? _action() : null;
            var binding = action != null ? InputBindingResolver.Primary(action) : default;
            string path = binding.Path ?? "";
            if (path == _boundPath) return;
            _boundPath = path;

            Sprite glyph = null;
            switch (binding.Mouse)
            {
                case MouseControl.Left: glyph = _art.MouseLeft; break;
                case MouseControl.Right: glyph = _art.MouseRight; break;
                case MouseControl.Middle: glyph = _art.MouseMiddle; break;
            }
            _mouseGlyph.sprite = glyph;
            _mouseGlyph.enabled = glyph != null;

            // A keyboard binding prints its cap when the small face can spell it; anything
            // longer than three letters would bury the icon, so it is left to the tooltip.
            string label = glyph == null && !string.IsNullOrEmpty(path) ? InputControlPaths.LabelForPath(path) : "";
            if (label.Length > 3 || !HudPixelFont.CanSpell(label, _art.Patterns(HudFontFace.Small))) label = "";
            _keyLabel.SetText(label.ToUpperInvariant());
        }

        /// <summary>The live binding's label for the tooltip, e.g. "Clic derecho" or "Q".</summary>
        public string BindingLabel
        {
            get
            {
                var action = _action != null ? _action() : null;
                return action != null ? InputBindingResolver.PrimaryLabel(action) : "";
            }
        }

        private static SpellDefinition ResolveSpell(SpellCaster caster, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var spell = caster != null ? caster.GetSpellByKey(key) : null;
            if (spell != null) return spell;
            // Not in the book: the character has not learned it. Still show WHAT it is, from the
            // catalogue, so the slot says "you could cast this" instead of being blank.
            if (ServiceLocator.TryGet<SpellCatalog>(out var catalog) && catalog != null)
                return catalog.GetByKey(key);
            return null;
        }
    }
}
