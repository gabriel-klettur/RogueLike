namespace Valkur.Core
{
    /// <summary>
    /// The music as it is WRITTEN, before the player's volume: what a visualiser needs, and
    /// what <see cref="IAudioService.GetMusicOutputData"/> cannot give. That one reads the
    /// source after <c>AudioSource.volume</c>, so with the music muted it returns silence and a
    /// visualiser draws a flat line — measured, peak 0.0000 with the music slider at 0.
    ///
    /// <para>A separate interface rather than two more members on <see cref="IAudioService"/>:
    /// every test double in the suite implements that one by hand, and a visualiser is the only
    /// reader. Resolve it with <c>ServiceLocator.Get&lt;IAudioService&gt;() as IMusicSignalSource</c>
    /// and treat null as "no signal".</para>
    /// </summary>
    public interface IMusicSignalSource
    {
        /// <summary>Output sample rate the signal is delivered at, in Hz.</summary>
        int MusicSignalSampleRate { get; }

        /// <summary>
        /// Copies the most recent mono samples of the active music source into
        /// <paramref name="dest"/>, oldest first, at the level the track was mastered at
        /// whatever the player's volume or the current duck. Returns how many samples were
        /// written; 0 when nothing is playing.
        /// </summary>
        int ReadMusicSignal(float[] dest);
    }
}
