using UnityEngine;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Where the interesting parts of a loading painting ARE, in the painting's own normalized
    /// space.
    ///
    /// <para><b>Normalized to the SPRITE, never to the canvas.</b> The loading background is
    /// fitted with <c>AspectRatioFitter.EnvelopeParent</c>, so a 2:1 window crops the top and
    /// bottom of a 3:2 painting and a taller one crops its sides. A point expressed in canvas
    /// pixels is therefore correct at exactly one window size and silently wrong at every other,
    /// which is the failure nobody reports because it only happens on somebody else's monitor.
    /// A fraction of the image survives every crop, and the effects are parented to the image's
    /// own <c>RectTransform</c> so the mapping is a multiply.</para>
    ///
    /// <para><b>A static table rather than a ScriptableObject</b>, the same call
    /// <c>InputControlPaths</c> makes: it is a closed set of measurements about art that ships
    /// with the game, it has to be readable from an EditMode test with no scene and no asset
    /// load, and an asset would add a `Resources/` entry whose only content is six floats.</para>
    ///
    /// <para>The numbers are MEASURED off the shipped PNG, not eyeballed: the plume's hot core
    /// was isolated by thresholding the 1536 x 1024 source for saturated fire (r &gt; 245,
    /// g &gt; 170, b &lt; 90, r - b &gt; 190) below the horizon, which returns a 568..822 by
    /// 450..639 band in pixels. Its right end is the mouth and its left end the far tip.</para>
    /// </summary>
    public static class LoadingArtAnchors
    {
        /// <summary>One painting's fire, as fractions of its own width and height, y UP.</summary>
        public readonly struct FireAnchor
        {
            /// <summary>Where the jet leaves the creature.</summary>
            public readonly Vector2 Mouth;

            /// <summary>Where it lands. Also where the smoke is born and where the wash sits.</summary>
            public readonly Vector2 Tip;

            /// <summary>Half-width of the jet at the mouth, as a fraction of the image's height.</summary>
            public readonly float MouthHalfWidth;

            /// <summary>Half-width where it lands. A jet SPREADS, so this is the larger one.</summary>
            public readonly float TipHalfWidth;

            public FireAnchor(Vector2 mouth, Vector2 tip, float mouthHalfWidth, float tipHalfWidth)
            {
                Mouth = mouth;
                Tip = tip;
                MouthHalfWidth = mouthHalfWidth;
                TipHalfWidth = tipHalfWidth;
            }

            /// <summary>True for an anchor that names a real jet rather than the empty default.</summary>
            public bool IsValid => (Tip - Mouth).sqrMagnitude > 1e-6f;

            /// <summary>A point on the jet's axis; 0 at the mouth, 1 where it lands.</summary>
            public Vector2 Axis(float t) => Vector2.LerpUnclamped(Mouth, Tip, t);

            /// <summary>Half-width at the same parameter, widening toward the far end.</summary>
            public float HalfWidthAt(float t) => Mathf.LerpUnclamped(MouthHalfWidth, TipHalfWidth, t);

            /// <summary>Unit vector from the mouth toward the landing point.</summary>
            public Vector2 Direction => (Tip - Mouth).normalized;
        }

        /// <summary>
        /// The anchor for a loading art, by the name the sprite is loaded under.
        ///
        /// <para>Answers an INVALID anchor for anything unmeasured rather than a guess at the
        /// middle of the screen: a jet of embers in the wrong place is worse than no jet, and a
        /// caller that checks <see cref="FireAnchor.IsValid"/> simply draws nothing. A second
        /// loading painting is a row here plus its own measurement, and nothing else.</para>
        /// </summary>
        public static FireAnchor FireFor(string artName)
        {
            switch (artName)
            {
                case "background_ini":
                    // 822/1536 = 0.535 across, 470/1024 = 0.459 down -> 0.541 up.
                    // 568/1536 = 0.370 across, 625/1024 = 0.610 down -> 0.390 up.
                    return new FireAnchor(new Vector2(0.535f, 0.541f),
                                          new Vector2(0.370f, 0.390f),
                                          mouthHalfWidth: 0.028f,
                                          tipHalfWidth: 0.072f);
                default:
                    return default;
            }
        }
    }
}
