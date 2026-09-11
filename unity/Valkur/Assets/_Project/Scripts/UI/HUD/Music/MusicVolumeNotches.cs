using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The music volume as a row of notches set into a sunken well. In pixel art a row of blocks
    /// reads better than a slider and lands on the grid by construction; the old slider's round
    /// knob was drawn as a tall oval because the whole panel was stretched 1.57x.
    ///
    /// <para><b>Click or drag</b> across the notches sets the level; the WHEEL steps it one notch
    /// at a time. Lighting up a notch means "at least this loud": a volume that is not zero
    /// always lights at least one, so a very quiet song never reads as a muted one.</para>
    ///
    /// <para><b>Unmuting refills the row a notch at a time</b> (40 ms each): the one animation
    /// here, and it answers an event the player caused.</para>
    /// </summary>
    public sealed class MusicVolumeNotches
    {
        private const float RefillStepSeconds = 0.04f;

        public RectTransform Root { get; }
        public MusicHudPointer Pointer { get; }

        private readonly Image[] _notches;
        private readonly Color _on, _off;
        private int _target;
        private float _shown;
        private bool _hot;

        /// <summary>Raised when the player picks a level, 0..1.</summary>
        public event Action<float> LevelPicked;

        /// <summary>Raised when the pointer enters (true) or leaves (false) the row.</summary>
        public event Action<bool> HoverChanged;

        public int Count => _notches.Length;

        /// <summary>Notches lit right now (animation included). For the tests.</summary>
        public int LitNow => Mathf.FloorToInt(_shown + 0.0001f);

        /// <summary>Notches the current volume should light.</summary>
        public int Target => _target;

        public bool Hot => _hot;

        public MusicVolumeNotches(Transform parent, MusicHudArt art, int x, int y, int count, Color on, Color off)
        {
            count = Mathf.Max(1, count);
            int innerW = count * 3 - 1;
            Root = HudRect.Make("Volume", parent, x, y, innerW + 2, 7);
            var well = HudRect.MakeImage("Well", Root, art.Well, 0, 0, innerW + 2, 7, Image.Type.Sliced);
            well.raycastTarget = true;
            _on = on;
            _off = off;
            _notches = new Image[count];
            for (int i = 0; i < count; i++)
            {
                _notches[i] = HudRect.MakeImage("Notch" + i, Root, art.White, 1 + i * 3, 1, 2, 5);
                _notches[i].color = off;
            }
            Pointer = Root.gameObject.AddComponent<MusicHudPointer>();
            Pointer.CapturesDrag = true;
            Pointer.Enter = () => { _hot = true; HoverChanged?.Invoke(true); };
            Pointer.Exit = () => { _hot = false; HoverChanged?.Invoke(false); };
            Pointer.Down = p => Pick(LevelAt(p.x));
            Pointer.Drag = p => Pick(LevelAt(p.x));
            Pointer.Scroll = dy => Step(dy > 0f ? 1 : dy < 0f ? -1 : 0);
        }

        /// <summary>Notches a 0..1 volume lights: nonzero lights at least one.</summary>
        public int NotchesFor(float volume01)
        {
            if (volume01 <= 0.001f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(volume01 * _notches.Length), 1, _notches.Length);
        }

        /// <summary>The level (0..count) a texel x across the row points at.</summary>
        public int LevelAt(float localX) => Mathf.Clamp(Mathf.CeilToInt((localX - 1f) / 3f), 0, _notches.Length);

        /// <summary>Picks a level as a click would. Public so a test can set it.</summary>
        public void Pick(int level)
        {
            level = Mathf.Clamp(level, 0, _notches.Length);
            LevelPicked?.Invoke((float)level / _notches.Length);
        }

        /// <summary>One notch up or down, as the wheel does.</summary>
        public void Step(int direction)
        {
            if (direction == 0) return;
            Pick(Mathf.Clamp(_target + direction, 0, _notches.Length));
        }

        /// <summary>Shows a volume. <paramref name="animateUp"/> refills from the current lit count.</summary>
        public void SetVolume(float volume01, bool animateUp)
        {
            int n = NotchesFor(volume01);
            if (n == _target && !animateUp) return;
            _target = n;
            if (!animateUp || n < _shown) _shown = n;
            Paint();
        }

        public void Tick(float dt)
        {
            if (_shown >= _target) return;
            _shown = Mathf.Min(_target, _shown + dt / RefillStepSeconds);
            Paint();
        }

        private void Paint()
        {
            int lit = LitNow;
            for (int i = 0; i < _notches.Length; i++)
            {
                var c = i < lit ? _on : _off;
                if (_notches[i].color != c) _notches[i].color = c;
            }
        }
    }
}
