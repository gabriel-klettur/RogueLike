using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One named LOOK for the particle title — the whole appearance of the word as data, so a
    /// second one is an entry in a list rather than a branch in the renderer.
    ///
    /// <para><b>Temperature is ONE axis, and that is the whole design.</b> The shipped title
    /// coloured each mote by how far it sat from the middle of its own stroke
    /// (<c>TitlePoint.Depth</c>), which draws a bar of hot metal: bright down the spine, cooling
    /// at the edges. Fire does not work that way — it is hottest at its BASE and cools as it
    /// rises, and it flickers. Two separate mechanisms would be two ways to be wrong, so both are
    /// folded into one number:</para>
    ///
    /// <code>
    /// temperature = 1 - depth * coolAcross - height01 * coolUpward + flicker
    /// colour      = cool -> warm -> hot, sampled at that temperature
    /// </code>
    ///
    /// <para>The shipped look is recovered exactly by <c>coolAcross = 1</c>, <c>coolUpward = 0</c>
    /// and <c>flicker = 0</c>, which is why adding fire did not change a pixel of it. A look that
    /// wants both — molten rock is hot in its cracks AND hotter at the foot — simply asks for
    /// both, and the renderer never learns that either exists.</para>
    ///
    /// <para><b>Red is the expensive colour and the halo is what pays for it.</b> Relative
    /// luminance weights green at 0.7152 and red at 0.2126, so the same alpha of red reaches the
    /// eye at under a third of the light — measured on the shipped cream word, contrast against
    /// the menu's own carousel already swings from 7.5:1 on the dark plate to 4.2:1 on the bright
    /// one, and a red word starts below that. Every look therefore carries its OWN halo strength
    /// rather than sharing one: the darker the ink, the more plate it needs under it.</para>
    /// </summary>
    [Serializable]
    public class TitleLook
    {
        [Tooltip("How this look is named in the style asset and in the console. Lowercase, no spaces.")]
        public string name = "ascua";

        [Header("Temperature ramp")]
        [Tooltip("The hottest tone — what the word is made of where it burns brightest.")]
        public Color hot = new Color(1f, 0.96f, 0.86f, 1f);

        [Tooltip("The middle of the ramp. Most of the word ends up near this, so it IS the colour " +
                 "the title reads as at a glance.")]
        public Color warm = new Color(1f, 0.80f, 0.38f, 1f);

        [Tooltip("The coldest tone, at the edge of a stroke and at the crown of a flame.")]
        public Color cool = new Color(0.98f, 0.48f, 0.16f, 1f);

        [Header("What cools a mote")]
        [Tooltip("Weight of the ACROSS-stroke axis: 1 draws a bar of hot metal, bright down its " +
                 "spine. This is the shipped title's only axis.")]
        [Range(0f, 1.5f)] public float coolAcross = 1f;

        [Tooltip("Weight of the VERTICAL axis, measured over the word's own cap height: 0 is a " +
                 "flat word, and anything above ~0.4 reads as flame, because the crown of every " +
                 "letter cools while its foot stays white-hot.")]
        [Range(0f, 1.5f)] public float coolUpward = 0f;

        [Header("Flicker")]
        [Tooltip("How far a mote's own temperature wanders, in ramp units. Fire is not a lamp: " +
                 "without this the word is a painted gradient however hot its colours are.")]
        [Range(0f, 0.8f)] public float flickerAmount = 0f;

        [Tooltip("Flicker cycles per second. Under ~2 it reads as breathing, over ~9 as static.")]
        [Range(0.1f, 14f)] public float flickerSpeed = 5.5f;

        [Tooltip("How much of the flicker is shared by the whole word rather than rolled per " +
                 "mote. Zero is a field of independent sparks; a little of it makes the word " +
                 "surge as one, which is what a real fire does when it draws air.")]
        [Range(0f, 1f)] public float flickerTogether = 0.35f;

        [Header("Drift")]
        [Tooltip("Amplitude of a mote's wander around its home, in canvas units.")]
        [Range(0f, 8f)] public float shimmerAmplitude = 1.5f;

        [Range(0.05f, 4f)] public float shimmerSpeed = 0.55f;

        [Tooltip("Shape of that wander. Below 1 the mote leans HORIZONTAL, which reads as heat " +
                 "shivering off a bar; above 1 it leans VERTICAL, which reads as rising.")]
        [Range(0.1f, 4f)] public float driftAspect = 0.45f;

        [Tooltip("A steady upward lean added to the drift, in canvas units. Fire rises; a bar of " +
                 "metal does not.")]
        [Range(-4f, 8f)] public float riseBias = 0f;

        [Header("Light")]
        [Tooltip("Multiplies the whole ramp. On an additive surface this is the intensity dial " +
                 "and it may exceed 1 — alpha is COVERAGE there, so reaching for alpha widens " +
                 "the word into fog instead of hardening it.")]
        [Range(0.2f, 3f)] public float glowGain = 1f;

        [Tooltip("The LEAST plate the word ever sits on. It used to be the only value — a " +
                 "constant under a carousel that changes every seven seconds, which is why the " +
                 "shipped title read at 7.5:1 over the dark frame and 4.2:1 over the bright one. " +
                 "It is a floor now: the plate measures what is behind it and may only add.")]
        [Range(0f, 1f)] public float haloStrength = 0.74f;

        [Tooltip("The contrast ratio the word holds against whatever the carousel is showing. " +
                 "The plate is SOLVED for this rather than tuned, so a red look and a cream one " +
                 "are equally legible without anybody re-tuning either.")]
        [Range(2f, 12f)] public float contrastTarget = 6f;

        [Tooltip("The most plate the word may ask for. Without a ceiling a bright frame is " +
                 "answered with a black bar drawn across the art.")]
        [Range(0.2f, 1f)] public float haloCeiling = 0.94f;

        [Range(0f, 1.5f)] public float haloPadding = 0.42f;

        [Tooltip("Colour of the plate. Black is the neutral answer; a deep red under a fire look " +
                 "makes the word sit in its own glow instead of on a hole.")]
        public Color haloTint = Color.black;

        [Header("Assembly and accents")]
        [Tooltip("The colour a mote wears while it is still flying in. Cooler than the word so " +
                 "the assembly reads as embers gathering rather than as the title sliding in.")]
        public Color ember = new Color(0.55f, 0.68f, 1f, 1f);

        [Tooltip("Seconds the word spends CATCHING FIRE once it has gathered, left to right. 0 " +
                 "means it arrives already lit, which is the shipped behaviour. This is the one " +
                 "beat that says what the game is: embers gather, and then the name ignites.")]
        [Range(0f, 4f)] public float ignitionSeconds = 0f;

        [Tooltip("Embers rising off the settled word, per second. Events, not a field: this is " +
                 "the one loop the menu is allowed, and it sits below the attention floor.")]
        [Range(0f, 60f)] public float emberRate = 7f;

        [Tooltip("Seconds between the light sweeps across the word. 0 turns them off.")]
        [Range(0f, 30f)] public float sweepInterval = 6.5f;

        [Range(0.2f, 4f)] public float sweepSeconds = 1.1f;

        /// <summary>
        /// The ramp, sampled. 0 is the coldest tone and 1 the hottest; the midpoint is
        /// <see cref="warm"/>, which is deliberately where most of the word lands.
        /// </summary>
        public Color Sample(float temperature)
        {
            float t = Mathf.Clamp01(temperature);
            Color c = t < 0.5f
                ? Color.Lerp(cool, warm, t * 2f)
                : Color.Lerp(warm, hot, (t - 0.5f) * 2f);
            if (glowGain != 1f)
            {
                // The COLOUR carries the intensity, never the alpha: on an additive surface alpha
                // is coverage, so a brighter word must be a brighter colour or it becomes a wider,
                // softer one. HDR values survive to the framebuffer here.
                c.r *= glowGain;
                c.g *= glowGain;
                c.b *= glowGain;
            }
            return c;
        }

        /// <summary>
        /// The temperature of one mote: across-stroke depth, height up the word, and its own
        /// flicker, in the one number the ramp is sampled at.
        /// </summary>
        public float TemperatureOf(float depth, float height01, float flicker)
            => 1f - depth * coolAcross - height01 * coolUpward + flicker;

        public TitleLook Clone() => (TitleLook)MemberwiseClone();
    }
}
