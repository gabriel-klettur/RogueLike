namespace Valkur.Gameplay.Interaction
{
    /// <summary>What a tap on the beat DID, separate from how well it was timed.</summary>
    public enum RhythmTapOutcome
    {
        /// <summary>Not counted at all: the debounce, a beat already lost, or no session to tap.</summary>
        Ignored = 0,

        /// <summary>The tap that starts tapping. It starts the metronome and never strikes.</summary>
        Started = 1,

        /// <summary>A cut landed a blow, worth whatever its grade is worth.</summary>
        Landed = 2,

        /// <summary>An early Bad or Awful cut with chances left: the axe is held back, try again.</summary>
        Retry = 3,

        /// <summary>A soquete: off the target, no blow, the beat is lost.</summary>
        Lost = 4,
    }
}
