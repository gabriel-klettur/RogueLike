namespace Valkur.Data
{
    /// <summary>
    /// Which billboard texture a particle preset renders with.
    ///
    /// Historically every Valkur particle was an untextured quad, which reads as a
    /// hard-edged square. These procedurally generated shapes give the VFX layer the
    /// soft, HD, glowing look the art direction calls for (the world is pixel-art;
    /// the VFX sorting layer deliberately is not).
    ///
    /// Textures are built once at runtime by <c>ParticleTextureLibrary</c> — no art
    /// assets, no atlas entries, no Resources footprint.
    /// </summary>
    public enum ParticleTextureShape
    {
        /// <summary>Pick a sensible shape from <c>kind</c> + <c>additive</c>. Default.</summary>
        Auto = 0,

        /// <summary>No texture — the legacy hard-edged quad. Rarely what you want.</summary>
        None = 1,

        /// <summary>Soft radial falloff. The general-purpose particle.</summary>
        SoftDot = 2,

        /// <summary>Bright plateau core with a wide bloom skirt. For light and magic.</summary>
        Glow = 3,

        /// <summary>Tight hot core, very fast falloff. For sparks, slashes, embers.</summary>
        Spark = 4,

        /// <summary>Cloudy value-noise puff. For smoke, dust, haze.</summary>
        Smoke = 5,

        /// <summary>Hollow annulus. For shockwaves, portal rims, ripples.</summary>
        Ring = 6,

        /// <summary>Four-point anamorphic flare. For sparkle and holy accents.</summary>
        Star = 7,

        // ── Long-axis shapes ──────────────────────────────────────────
        //
        // Everything above is radial. These two are the first silhouettes in the library
        // with a long axis, a tip and a stem, and they exist because vegetation had no
        // honest shape: every falling leaf, petal and pollen mote in Valkur rendered as a
        // SoftDot blob and faked tumbling by oscillating the quad's width.
        //
        // NEVER REORDER OR RENUMBER A MEMBER OF THIS ENUM. The 131 particle preset assets
        // serialize `textureShape` BY NUMBER, so shifting a value silently repoints every
        // one of them at a different texture — a change that compiles, passes every test,
        // and only surfaces later as "the smoke is a ring now". New shapes are APPENDED
        // with the next free explicit value; that is also why Auto keeps 0 instead of being
        // sorted anywhere more sensible.

        /// <summary>
        /// Pointed ovate leaf, long axis on +Y: widest at ~42% of the blade, drawn out to a
        /// tip, rounded into a short petiole, with a darker midrib down the middle.
        ///
        /// The ~2.1:1 proportion is baked into the TEXTURE, so a preset reads as a leaf at
        /// the default <c>sizeAspect = 1</c>. sizeAspect multiplies it — take that below 1
        /// only to deliberately narrow the blade, not to "make it leaf-shaped".
        /// </summary>
        Leaf = 8,

        /// <summary>
        /// Soft teardrop petal, long axis on +Y: broad and blunt at the outer edge, narrowing
        /// to a point at the base. No midrib, and a softer edge than <see cref="Leaf"/> at
        /// every softness setting — a petal is translucent, a leaf is a cut-out. ~1.9:1 in
        /// the texture, same sizeAspect caveat as Leaf.
        /// </summary>
        Petal = 9,

        // ── Structured field shapes ───────────────────────────────────
        //
        // Radial and long-axis shapes are both single blobs: whatever they depict, one
        // particle carries one silhouette and the effect comes from having many. Vortex is
        // the first shape meant to be used ALONE — one quad, spun by rotationOverLifetime,
        // IS the effect. That is why it needs no motion fields to read as motion.

        /// <summary>
        /// Two-armed logarithmic spiral in a disc, arms converging into a bright hub, faded
        /// out at the rim so the quad has no visible edge.
        ///
        /// Built to be spun rather than swarmed: a single long-lived particle with
        /// <c>rotationSpeedDegrees</c> reads as matter turning inward, which is the one
        /// thing a cloud of billboards cannot express — every particle system in Valkur
        /// draws its particles as unconnected dots, so a spiral made of dots is a ring of
        /// dots. Pair it with <c>rotationOneWay</c>: the default spin randomises its sign
        /// per particle, and two overlapping copies turning opposite ways cancel into a
        /// flicker.
        ///
        /// The texture is circular. An oval gate comes from <c>sizeAspect</c>, which
        /// squashes the quad without touching the arm spacing.
        /// </summary>
        Vortex = 10,
    }
}
