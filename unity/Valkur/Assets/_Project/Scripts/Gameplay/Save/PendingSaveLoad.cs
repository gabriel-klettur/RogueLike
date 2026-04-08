namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Static carrier for a save path that should be loaded after scene transition.
    /// The gameplay scene startup code checks this and calls SaveService.Load() if set.
    /// </summary>
    public static class PendingSaveLoad
    {
        public static string Path { get; set; }

        public static bool HasPending => !string.IsNullOrEmpty(Path);

        public static string Consume()
        {
            string p = Path;
            Path = null;
            return p;
        }
    }
}
