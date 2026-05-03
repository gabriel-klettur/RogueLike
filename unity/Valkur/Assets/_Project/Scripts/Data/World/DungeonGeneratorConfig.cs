using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Data-driven configuration for procedural dungeon generation.
    /// Maps to Python's global_map_settings dungeon parameters.
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonGeneratorConfig", menuName = "Valkur/Dungeon Generator Config")]
    public class DungeonGeneratorConfig : ScriptableObject
    {
        [Header("Zone Dimensions")]
        [Tooltip("Width of a zone in tiles")]
        [SerializeField] private int zoneWidth = 50;

        [Tooltip("Height of a zone in tiles")]
        [SerializeField] private int zoneHeight = 50;

        [Header("Room Settings")]
        [Tooltip("Maximum number of room placement attempts")]
        [SerializeField] private int maxRoomAttempts = 10;

        [Tooltip("Maximum number of rooms allowed. 0 means use maxRoomAttempts as limit.")]
        [SerializeField] private int maxRoomsAllowed = 0;

        [Tooltip("Minimum room size in tiles")]
        [SerializeField] private int roomMinSize = 10;

        [Tooltip("Maximum room size in tiles")]
        [SerializeField] private int roomMaxSize = 20;

        [Header("Tunnel Settings")]
        [Tooltip("Thickness of tunnels connecting rooms")]
        [SerializeField] private int tunnelThickness = 3;

        [Header("Tile Characters")]
        [Tooltip("Character used for wall tiles")]
        [SerializeField] private char wallChar = '#';

        [Tooltip("Character used for room floor tiles")]
        [SerializeField] private char roomChar = 'O';

        [Tooltip("Character used for tunnel floor tiles")]
        [SerializeField] private char tunnelChar = '=';

        public int ZoneWidth => zoneWidth;
        public int ZoneHeight => zoneHeight;
        public int MaxRoomAttempts => maxRoomAttempts;
        public int MaxRoomsAllowed => maxRoomsAllowed;
        public int RoomMinSize => roomMinSize;
        public int RoomMaxSize => roomMaxSize;
        public int TunnelThickness => tunnelThickness;
        public char WallChar => wallChar;
        public char RoomChar => roomChar;
        public char TunnelChar => tunnelChar;
    }
}
