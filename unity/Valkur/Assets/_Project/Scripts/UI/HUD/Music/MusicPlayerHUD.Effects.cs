using UnityEngine;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // Every effect here answers an EVENT and dies within a second. The table of which event
        // earns what is in .github/MUSIC_HUD_BEAUTY_AUDIT_2026-09-11.md, section 6.

        private float _medallionFlashT;
        private float _shineT = -1f;
        private int _shineWidth;
        private float _sigilPopT;

        /// <summary>How lit the medallion's rim is, 0..1. For the tests.</summary>
        public float MedallionFlash => _medallionFlashT;

        /// <summary>True while the title's shine is crossing it. For the tests.</summary>
        public bool ShineActive => _shineT >= 0f;

        private Color RimRest => Color.Lerp(_theme.stoneLight, Color.white, 0.38f);

        private void TickEffects(float dt)
        {
            // The medallion's rim turns gold for a new track and cools back to stone.
            if (_medallionFlashT > 0f)
            {
                _medallionFlashT = Mathf.Max(0f, _medallionFlashT - dt / Mathf.Max(0.05f, _style.medallionFlashSeconds));
                float k = _medallionFlashT * _medallionFlashT;
                _medallionRim.color = Color.Lerp(RimRest, _theme.gold, k);
                var g = _theme.gold;
                g.a = 0.55f * k;
                _medallionGlow.color = g;
            }
            else if (_medallionRim.color != RimRest)
            {
                _medallionRim.color = RimRest;
                var g = _theme.gold;
                g.a = 0f;
                _medallionGlow.color = g;
            }

            // The sigil pops one texel up when the zone's list changes, and settles.
            if (_sigilPopT > 0f)
            {
                _sigilPopT = Mathf.Max(0f, _sigilPopT - dt);
                int lift = _sigilPopT > 0.18f ? 1 : 0;
                HudRect.Place(_sigil.rectTransform, Inset + 3, MedallionY + 3 + lift, 9, 9);
                _sigil.color = Color.Lerp(_theme.textDim, _theme.gold, Mathf.Clamp01(_sigilPopT / 0.35f));
            }

            // A shine crosses the title, whole texels at a time.
            if (_shineT >= 0f)
            {
                _shineT += dt / Mathf.Max(0.05f, _style.shineSeconds);
                if (_shineT >= 1f)
                {
                    _shineT = -1f;
                    _shine.enabled = false;
                }
                else
                {
                    int x = _titleX - 2 + Mathf.RoundToInt(_shineT * (_shineWidth + 4));
                    HudRect.Place(_shine.rectTransform, x, TitleRowY, 3, RowH);
                    var c = Color.white;
                    c.a = Mathf.Sin(_shineT * Mathf.PI) * 0.8f;
                    _shine.color = c;
                    _shine.enabled = true;
                }
            }
        }

        /// <summary>A new track: gold on the medallion, notes rising from it, light across the title.</summary>
        private void AnnounceTrack()
        {
            _medallionFlashT = 1f;
            _shineT = 0f;
            _shineWidth = Mathf.Max(8, _title.InkWidth);
            // The notes leave from the medallion's RIM, fanned over its upper half, each outward
            // along its own radius and drifting up. Launched from one point they clumped into a
            // shape that read as a crown (the first live capture), not as notes.
            var centre = new Vector2(Inset + MedallionSize * 0.5f, MedallionY + MedallionSize * 0.5f);
            int n = _style.notesOnTrack;
            for (int i = 0; i < n; i++)
            {
                float k = n > 1 ? i / (float)(n - 1) : 0.5f;
                float a = Mathf.Lerp(160f, 20f, k) * Mathf.Deg2Rad + Random.Range(-0.12f, 0.12f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var v = dir * Random.Range(16f, 24f) + new Vector2(0f, 10f);
                var c = i % 2 == 0 ? _theme.gold : Color.Lerp(_theme.gold, Color.white, 0.5f);
                _motes.Emit(centre + dir * 7f, v, c, _style.moteLifeSeconds * Random.Range(0.85f, 1.15f),
                            HudMoteShape.Note, gravity: -6f, drag: 1.6f);
            }
        }

        /// <summary>A new zone's list: the sigil pops and throws a small ring of stars.</summary>
        private void AnnounceZone()
        {
            _sigilPopT = 0.35f;
            var c = new Vector2(Inset + MedallionSize * 0.5f, MedallionY + MedallionSize * 0.5f);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(c + dir * 6f, dir * 18f, _theme.gold, _style.moteLifeSeconds * 0.7f,
                            HudMoteShape.Plus, drag: 3f);
            }
        }

        /// <summary>Resumed: two sparks leave the play key. Pausing, by design, makes no light.</summary>
        private void OnResumed()
        {
            var p = _play.Centre;
            for (int i = 0; i < 2; i++)
                _motes.Emit(p + new Vector2(0f, 4f), new Vector2(Random.Range(-8f, 8f), 20f), _theme.text,
                            _style.moteLifeSeconds * 0.6f, HudMoteShape.Dot, drag: 2f);
        }

        /// <summary>A skip: sparks leave the key in the direction of the jump.</summary>
        private void BurstFromKey(MusicHudKey key, int direction)
        {
            // A skip from the keyboard with the panel closed has nobody to show the sparks to;
            // left in the pool they would fly out, stale, the next time it opens.
            if (_hidden) return;
            var p = key.Centre;
            for (int i = 0; i < _style.motesOnSkip; i++)
                _motes.Emit(p, new Vector2(direction * (26f + i * 8f), Random.Range(-6f, 10f)), _theme.gold,
                            _style.moteLifeSeconds * 0.6f, i == 0 ? HudMoteShape.Star : HudMoteShape.Dot, drag: 3f);
        }

        /// <summary>A seek lands: a small burst from the bead where the song now is.</summary>
        private void BurstFromBead()
        {
            var p = _groove.BeadCentre;
            int n = _style.motesOnSeek;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 2f + 0.4f;
                _motes.Emit(p, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 16f, _theme.gold,
                            _style.moteLifeSeconds * 0.5f, HudMoteShape.Plus, drag: 4f);
            }
        }

        /// <summary>Unmuted: notes leave the speaker and the notches refill one by one.</summary>
        private void OnUnmuted()
        {
            _unmutePending = true;
            var p = _mute.Centre + new Vector2(2f, 3f);
            for (int i = 0; i < _style.notesOnUnmute; i++)
                _motes.Emit(p, new Vector2(8f + i * 6f, 18f + i * 4f), _theme.text, _style.moteLifeSeconds,
                            HudMoteShape.Note, gravity: -4f, drag: 1.2f);
        }

        /// <summary>
        /// A downbeat while the resonance is open: the tallest bar lets one mote go. Once per BAR,
        /// never per beat — a spark on every beat is a fountain.
        /// </summary>
        private void OnBar(int bar)
        {
            if (!_expanded || _hidden || _resonance == null) return;
            int band = _resonance.TallestBand;
            if (band < 0) return;
            // Bars in the graphic: 3 texels wide, 1 apart, centred in its rect; the column top
            // is where the light leaves from.
            var rt = _resonance.rectTransform;
            int w = Mathf.RoundToInt(rt.sizeDelta.x);
            int bandsW = _style.bandCount * 4 - 1;
            float x = rt.anchoredPosition.x + _resonanceRoot.anchoredPosition.x + (w - bandsW) / 2 + band * 4 + 1.5f;
            float y = rt.anchoredPosition.y + _resonanceRoot.anchoredPosition.y
                      + MusicResonanceGraphic.BandsBaseY + _resonance.BlocksIn(band) * 2;
            _motes.Emit(new Vector2(x, y), new Vector2(0f, 14f), _style.bandLight, _style.moteLifeSeconds,
                        HudMoteShape.Dot, drag: 1f);
        }
    }
}
