using UnityEngine;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// The three glyphs a <see cref="QuestGiverBadge"/> can show, drawn in code and shared.
    ///
    /// <para><b>Generated rather than imported</b> for the reason <c>ElementalSprites</c>
    /// records: these are a handful of opaque pixels, and an authored sprite would cost an
    /// import, an atlas slot and a lookup that can come back null. A texture built once and
    /// held statically cannot fail to resolve.</para>
    ///
    /// <para><b>WHITE, and tinted by the renderer.</b> One glyph per shape rather than one
    /// per shape-and-colour: the meaning is carried by the tint the badge applies, so a new
    /// state costs a colour rather than a texture.</para>
    ///
    /// <para><b>`FullRect`, never the default.</b> <c>Sprite.Create</c> defaults to
    /// <c>SpriteMeshType.Tight</c>, which traces the alpha outline to fit a mesh — free on
    /// a 16x32 texture, and the habit is what cost this project 60 % of its boot when the
    /// same call was pointed at an atlas page.</para>
    /// </summary>
    public static class QuestBadgeSprites
    {
        private const int W = 16;
        private const int H = 32;
        private const int PPU = 32;

        private static Sprite _offer;
        private static Sprite _turnIn;
        private static Sprite _inProgress;

        /// <summary>The glyph for <paramref name="state"/>, built on first use.</summary>
        public static Sprite For(QuestBadgeState state)
        {
            switch (state)
            {
                case QuestBadgeState.TurnIn:
                    return _turnIn != null ? _turnIn : (_turnIn = BuildQuestionMark());
                case QuestBadgeState.InProgress:
                    return _inProgress != null ? _inProgress : (_inProgress = BuildEllipsis());
                default:
                    return _offer != null ? _offer : (_offer = BuildExclamation());
            }
        }

        // ── Glyphs ──────────────────────────────────────────────────────────

        private static Sprite BuildExclamation()
        {
            var px = Blank();
            // Tapered stem, then a gap, then the dot — the taper is what reads as a mark
            // rather than as a bar at sixteen pixels wide.
            FillRect(px, 6, 11, 4, 17);
            FillRect(px, 7, 9,  2, 2);
            FillRect(px, 6, 2,  4, 4);
            return Finish(px);
        }

        private static Sprite BuildQuestionMark()
        {
            var px = Blank();
            FillRect(px, 5,  25, 6, 3);   // top bar
            FillRect(px, 10, 20, 3, 6);   // right shoulder
            FillRect(px, 6,  17, 6, 3);   // middle
            FillRect(px, 6,  11, 3, 7);   // stem
            FillRect(px, 6,  2,  4, 4);   // dot
            return Finish(px);
        }

        private static Sprite BuildEllipsis()
        {
            var px = Blank();
            FillRect(px, 2,  13, 3, 3);
            FillRect(px, 7,  13, 3, 3);
            FillRect(px, 12, 13, 3, 3);
            return Finish(px);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Color32[] Blank()
        {
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
            return px;
        }

        private static void FillRect(Color32[] px, int x, int y, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
            {
                int yy = y + dy;
                if (yy < 0 || yy >= H) continue;
                for (int dx = 0; dx < w; dx++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= W) continue;
                    px[yy * W + xx] = new Color32(255, 255, 255, 255);
                }
            }
        }

        private static Sprite Finish(Color32[] px)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                // Named so it is identifiable in a memory profile rather than showing up
                // as one of a hundred anonymous Texture2D rows.
                name = "QuestBadgeGlyph",
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);

            return Sprite.Create(
                tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), PPU,
                0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Domain Reload is OFF, so these survive into the next Play session pointing at
        /// textures Unity destroyed on exit — a destroyed sprite passes a <c>!= null</c>
        /// check for exactly as long as it takes to throw on first use. Bare assignments,
        /// because <c>DomainReloadStaticResetTests</c> reads this method's raw IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _offer = null;
            _turnIn = null;
            _inProgress = null;
        }
    }
}
