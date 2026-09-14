using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Where the rows sit, how wide they are, and which order they draw in.
    ///
    /// <para>Split out because it is the half that used to be copied: every one of the three old
    /// bars computed its own stack position from private copies of the others' heights. There is
    /// one stack now and one place that lays it out.</para>
    /// </summary>
    public sealed partial class WorldBarRig
    {
        /// <summary>
        /// Measure the creature and rebuild the stack.
        ///
        /// <para>Measured EVERY time this is called rather than once in <c>Awake</c>: a loadout
        /// swap replaces four sprite sets, a monster's <c>scaleConfig</c> resizes it, and the old
        /// bars kept the height they read on the frame they were born on.</para>
        /// </summary>
        private void Relayout()
        {
            _layoutDirty = false;
            var style = WorldBarStyle.Active;

            float bodyTopWorld = 1.4f;   // a ~22 px sprite at PPU 16, the historical fallback
            float bodyWidthWorld = WorldBarGeometry.Texels(style.fallbackWidthTexels);
            float bodyHeightWorld = bodyTopWorld;

            if (_body != null && _body.sprite != null)
            {
                var b = _body.bounds;
                bodyTopWorld = b.max.y - transform.position.y;
                bodyWidthWorld = b.size.x;
                bodyHeightWorld = b.size.y;
            }

            // The rig hangs off an entity that may be scaled by its own scaleConfig, and a bar
            // that scaled with the creature would be unreadable on a barbol_baby and enormous on
            // a colossus. Inverting the parent's scale here makes one local unit exactly one
            // world unit inside the rig, which is what lets every size below be stated in texels.
            float parentScaleY = transform.localScale.y;
            if (parentScaleY > 0f && !Mathf.Approximately(parentScaleY, 1f))
            {
                float inv = 1f / parentScaleY;
                _root.localPosition = new Vector3(0f, bodyTopWorld * inv, 0f);
                _root.localScale = new Vector3(inv, inv, 1f);
            }
            else
            {
                _root.localPosition = new Vector3(0f, bodyTopWorld, 0f);
                _root.localScale = Vector3.one;
            }

            int widthTexels = style.widthFollowsBody
                ? WorldBarGeometry.WidthTexelsForBody(bodyWidthWorld, bodyHeightWorld,
                                                      style.maxWidthFractionOfHeight,
                                                      style.minWidthTexels, style.maxWidthTexels)
                : MakeEven(style.fallbackWidthTexels);
            _barWidth = WorldBarGeometry.Texels(widthTexels);

            float y = WorldBarGeometry.Texels(style.headMarginTexels);
            // Negative on purpose: at -1 the resource row's bottom outline IS the health row's top
            // outline, so the two rows are one instrument instead of two strips with a slot of the
            // character's own pixels showing between them.
            float gap = WorldBarGeometry.Texels(style.rowGapTexels);
            float t = WorldBarGeometry.TEXEL;

            // Health sits CLOSEST to the head. It is the value the player acts on, so it gets the
            // position the eye reaches first and the only row wide enough to carry quarter marks.
            float healthH = style.HealthRowHeight;
            bool notches = style.showQuarterNotches && widthTexels >= style.notchMinWidthTexels;
            _health.Layout(_barWidth, 0f, y + healthH * 0.5f, notches);
            y += healthH;
            float top = y;

            // The energy row (Carrera stamina) sits BETWEEN health and the resource row, sharing
            // health's top outline the same way the resource row used to be the only thing that
            // did. It always carries its quarter notches — that is the shape distinction from
            // mana a colour-blind player needs, not gated by showQuarterNotches, which is a
            // health-only toggle.
            bool energyOn = _wantsEnergy && _energy != null;
            float energySeamWidth = 0f, energySeamY = 0f;
            if (energyOn)
            {
                y += gap;
                float energyH = style.EnergyRowHeight;
                float centre = y + energyH * 0.5f;
                if (style.rowGapTexels < 0)
                {
                    energySeamWidth = _barWidth;
                    energySeamY = y + t * 0.5f;
                }
                bool energyNotches = widthTexels >= style.notchMinWidthTexels;
                _energy.Layout(_barWidth, 0f, centre, energyNotches);
                y += energyH;
                top = Mathf.Max(top, y);
            }
            _energyJoints.Layout(energySeamWidth, energySeamY, float.NaN, 0f);

            float seamWidth = 0f, seamY = 0f;
            float cornerX = float.NaN, cornerY = 0f;

            bool manaOn = _wantsMana && _mana != null;
            bool dashOn = _wantsDash && _pip != null;
            if (manaOn || dashOn)
            {
                y += gap;
                float rowH = style.ResourceRowHeight;
                float centre = y + rowH * 0.5f;

                float pipSide = dashOn ? _pip.Side : 0f;
                if (style.rowGapTexels < 0)
                {
                    seamWidth = _barWidth;
                    seamY = y + t * 0.5f;
                }

                if (manaOn)
                {
                    // The mana bar runs right up to the pip: its outline and the pip's sit side by
                    // side, so the row is one closed frame with no ground showing through it.
                    int manaTexels = widthTexels - WorldBarGeometry.TexelsOf(pipSide);
                    manaTexels = MakeEvenDown(Mathf.Max(4, manaTexels));
                    float manaW = WorldBarGeometry.Texels(manaTexels);
                    // Left-aligned inside the row, so the pip owns the right end whether or not
                    // the mana bar happens to be an even number of texels narrower than the row.
                    float manaCentre = -_barWidth * 0.5f + manaW * 0.5f;
                    _mana.Layout(manaW, manaCentre, centre, notchesWanted: false);
                    if (dashOn)
                    {
                        cornerX = -_barWidth * 0.5f + manaW - t * 0.5f;
                        cornerY = y + rowH - t * 0.5f;
                    }
                }

                if (dashOn)
                {
                    // Standing ON the row's floor rather than centred on it: the pip is taller than
                    // the thin row, and centring it pushed its lower half into the health row. Stood
                    // on the shared outline it rises above the row instead, as the stack's one
                    // ornament — the corner a gem belongs in.
                    _pip.Layout(_barWidth * 0.5f - pipSide * 0.5f, y + pipSide * 0.5f);
                    top = Mathf.Max(top, y + pipSide);
                }

                y += rowH;
                top = Mathf.Max(top, y);
            }

            _joints.Layout(seamWidth, seamY, cornerX, cornerY);
            _status.Layout(top + WorldBarGeometry.Texels(style.statusGapTexels) + _status.Height * 0.5f);

            PushAlpha();
        }

        private static int MakeEven(int v) => (v & 1) == 0 ? v : v + 1;
        private static int MakeEvenDown(int v) => (v & 1) == 0 ? v : v - 1;

        /// <summary>
        /// Give every renderer an order derived from the OWNER's Y, the same formula
        /// <c>YSortEntity</c> uses for the body.
        ///
        /// <para>The old bars used a constant 200..212 for every creature in the world, so the
        /// health bar of a monster standing at the back drew over the health bar of one standing
        /// in front. Deriving it means two bars sort the way their owners do.</para>
        ///
        /// <para>The rig's <see cref="SORT_SPAN"/> slots span about 0.3 world units of Y
        /// granularity, so two creatures standing closer than that on Y can interleave their bars
        /// — which is bounded, local, and strictly better than the constant that gave them no order
        /// at all.</para>
        /// </summary>
        private void SyncSorting()
        {
            float y = transform.position.y;
            if (!float.IsNaN(_lastSortY) && Mathf.Abs(y - _lastSortY) < Y_RESORT_THRESHOLD) return;
            _lastSortY = y;

            _sortBase = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, y);
            _health.SetSortingBase(_sortBase + SORT_HEALTH);
            _mana?.SetSortingBase(_sortBase + SORT_RESOURCE);
            _energy?.SetSortingBase(_sortBase + SORT_ENERGY);
            _pip?.SetSortingBase(_sortBase + SORT_PIP);
            _joints.SetSortingBase(_sortBase + SORT_JOINT);
            _energyJoints.SetSortingBase(_sortBase + SORT_ENERGY_JOINT);
            _status.SetSortingBase(_sortBase + SORT_STATUS);
            // The sparks too: left at their construction order they sat a thousand below the bars
            // they were thrown off, i.e. under every readout in the scene.
            _sparks?.SetSortingOrder(_sortBase + SORT_SPARKS);
        }

        /// <summary>Push the palette for the current rank into every row.</summary>
        private void ApplyColours()
        {
            if (!_built) return;
            var style = WorldBarStyle.Active;

            Color fill = _hasColourOverride ? _healthFillOverride : style.HealthFor(_rank);
            Color low = _hasColourOverride ? _healthLowOverride : style.LowFor(_rank);
            Color cap = style.CapFor(_rank);
            Color halo = style.HaloFor(_rank);

            _health.SetColours(fill, low, style.healthChip, style.outline, style.plate, style.notch,
                               style.lowThreshold);
            _health.SetRankColours(cap, halo, style.lowPulse);
            _health.SetPlateVisible(style.drawPlate);

            // The mana row never turns "low": running out of mana is a resource decision, not a
            // warning, and a second colour changing under the health bar would compete with the
            // one that is. It carries the rank's caps but not its halo — one halo per creature
            // is a statement, two is a pattern.
            _mana?.SetColours(style.mana, style.mana, style.manaSpent, style.outline, style.plate,
                              style.notch, 0f);
            _mana?.SetRankColours(cap, Color.clear, style.lowPulse);
            _mana?.SetPlateVisible(style.drawPlate);

            // Same story as mana: energy never switches to a "low" hue on its own, since it is
            // Player-only and its own heartbeat-style event (the row shake) already announces
            // running out. Shape (the quarter notches) carries what colour cannot for a
            // colour-blind player.
            var energyFill = style.EnergyFillColour;
            _energy?.SetColours(energyFill, energyFill, style.EnergySpentColour, style.outline,
                                style.plate, style.notch, 0f);
            _energy?.SetRankColours(cap, Color.clear, style.lowPulse);
            _energy?.SetPlateVisible(style.drawPlate);
            _energyJoints.SetColour(style.outline);

            _pip?.SetColours(style.dashReady, style.dashCharging, style.outline, style.plate);
            _joints.SetColour(style.outline);
        }
    }

    /// <summary>
    /// How many screen pixels one world unit is, resolved once per frame for every bar in the
    /// scene.
    ///
    /// <para>Each rig needs it to quantise its fill to a whole pixel, and asking
    /// <c>Camera.main</c> per rig per frame is a <c>FindGameObjectWithTag</c> behind a property.
    /// One resolve per frame, shared.</para>
    /// </summary>
    internal static class WorldBarPixelGrid
    {
        private static int s_frame = -1;
        private static float s_ppu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorldBarPixelGridStatics()
        {
            s_frame = -1;
            s_ppu = 0f;
        }

        /// <summary>
        /// Pixels per world unit, or 0 when there is no orthographic camera — which callers read
        /// as "do not quantise" rather than dividing by it.
        /// </summary>
        public static float PixelsPerUnit
        {
            get
            {
                if (s_frame == Time.frameCount) return s_ppu;
                s_frame = Time.frameCount;
                var cam = Camera.main;
                s_ppu = cam != null && cam.orthographic
                    ? WorldBarGeometry.PixelsPerUnit(cam.pixelHeight, cam.orthographicSize)
                    : 0f;
                return s_ppu;
            }
        }
    }
}
