namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Compass orientation of a room doorway. Two adjacent doorways must face
    /// opposite directions (Nâ†”S, Eâ†”W) for the dungeon builder to align them.
    /// </summary>
    public enum Orientation
    {
        North,
        East,
        South,
        West,
        None,
    }
}
