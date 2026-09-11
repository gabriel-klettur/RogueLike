namespace Valkur.Gameplay
{
    /// <summary>
    /// The seam for commands registered from an assembly that cannot see the console's
    /// internals — <c>Valkur.UI</c> references Gameplay, so it can call
    /// <see cref="RegisterCommand"/>, but <c>Log</c> is private and an external command had no
    /// way to answer on the console it was typed into. Its output would have gone to the Unity
    /// log, which a player never sees.
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>Write one line of output to the console panel.</summary>
        public void Print(string message) => Log(message);
    }
}
