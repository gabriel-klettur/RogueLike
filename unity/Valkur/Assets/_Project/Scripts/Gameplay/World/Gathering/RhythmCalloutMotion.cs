namespace Valkur.Gameplay.World
{
    /// <summary>
    /// How a judgement word moves. Motion is read before the word: a good cut RISES, a weak cut
    /// SAGS (it landed, badly), and a retry or a soquete SHAKES (it did not land).
    /// </summary>
    public enum RhythmCalloutMotion
    {
        Rise = 0,
        Sag = 1,
        Shake = 2,
    }
}
