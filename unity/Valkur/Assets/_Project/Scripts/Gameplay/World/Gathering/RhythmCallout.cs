using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>What the judgement callout says and how loudly, for one tap.</summary>
    public readonly struct RhythmCallout
    {
        public readonly string Text;
        public readonly Color Colour;

        /// <summary>1 for an ordinary judgement; above 1 for one the screen should shout.</summary>
        public readonly float Weight;

        public readonly RhythmCalloutMotion Motion;

        public RhythmCallout(string text, Color colour, float weight, RhythmCalloutMotion motion)
        {
            Text = text;
            Colour = colour;
            Weight = weight;
            Motion = motion;
        }

        /// <summary>Anything that did not rise: a weak cut, a retry, a soquete.</summary>
        public bool IsMiss => Motion != RhythmCalloutMotion.Rise;

        public bool IsEmpty => string.IsNullOrEmpty(Text);
    }
}
