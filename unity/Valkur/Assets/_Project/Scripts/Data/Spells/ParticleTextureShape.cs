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
    }
}
