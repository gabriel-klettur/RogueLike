using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.UI
{
    /// <summary>What a world marker is FOR. Drives its colour and glyph on the minimap.</summary>
    public enum WorldMarkerKind
    {
        /// <summary>A character with work to hand out.</summary>
        QuestOffer = 0,

        /// <summary>A character waiting to be told a finished quest is finished.</summary>
        QuestTurnIn = 1,

        /// <summary>Somewhere an active objective wants the player to be.</summary>
        QuestObjective = 2,
    }

    /// <summary>One point of interest, in world space.</summary>
    public readonly struct WorldMarker
    {
        public readonly Vector2 Position;
        public readonly WorldMarkerKind Kind;

        /// <summary>Short caption drawn beside the marker. May be empty.</summary>
        public readonly string Label;

        public WorldMarker(Vector2 position, WorldMarkerKind kind, string label = null)
        {
            Position = position;
            Kind = kind;
            Label = label ?? string.Empty;
        }
    }

    /// <summary>
    /// A noticeboard of world points that one assembly computes and another draws.
    ///
    /// <para><b>Why it exists.</b> The minimap is <c>Valkur.UI</c> and the quest layer is
    /// <c>Valkur.Gameplay</c>, and NEITHER may reference the other — `Gameplay -> UI` is
    /// the forbidden circular edge and UI does not reference Gameplay either. So the one
    /// system that knows where the player should go and the one system that can draw a
    /// dot on a map could not be introduced. That is why quest navigation scored 0.5:
    /// not because it was hard, because there was no channel.</para>
    ///
    /// <para><b>Plain data, not components.</b> <c>MinimapMarker</c> is a MonoBehaviour, so
    /// publishing through it would mean Gameplay creating a UI component — the same wall.
    /// A struct with a position and a reason crosses the boundary because <c>Valkur.Core</c>
    /// is below both.</para>
    ///
    /// <para><b>Keyed by CHANNEL from the first day, with one publisher.</b> A board that
    /// simply replaced its whole contents would work today and silently erase the quest
    /// markers the moment anything else — a ping, a treasure map, a boss telegraph — wanted
    /// to publish one. The failure would be a marker that vanishes for no reason the player
    /// or the author can see, which is the shape this project keeps paying for.</para>
    /// </summary>
    public static class WorldMarkerBoard
    {
        private static readonly Dictionary<string, List<WorldMarker>> _channels =
            new Dictionary<string, List<WorldMarker>>();

        // Rebuilt only when a channel changes, so the draw loop iterates one flat list
        // rather than walking a dictionary of lists every frame.
        private static List<WorldMarker> _flat = new List<WorldMarker>();
        private static bool _dirty;

        /// <summary>Every published marker, from every channel.</summary>
        public static IReadOnlyList<WorldMarker> All
        {
            get
            {
                if (_dirty) Rebuild();
                return _flat;
            }
        }

        /// <summary>
        /// Replace everything <paramref name="channel"/> has published. Passing an empty
        /// list is how a channel says "nothing right now" without unregistering.
        /// </summary>
        public static void Publish(string channel, IReadOnlyList<WorldMarker> markers)
        {
            if (string.IsNullOrEmpty(channel)) return;

            if (!_channels.TryGetValue(channel, out var list))
            {
                list = new List<WorldMarker>();
                _channels[channel] = list;
            }

            list.Clear();
            if (markers != null) list.AddRange(markers);
            _dirty = true;
        }

        /// <summary>Drop a channel entirely. Safe for one that never published.</summary>
        public static void Clear(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            if (_channels.Remove(channel)) _dirty = true;
        }

        private static void Rebuild()
        {
            _flat.Clear();
            foreach (var kv in _channels) _flat.AddRange(kv.Value);
            _dirty = false;
        }

        /// <summary>
        /// Domain Reload is OFF, so a board left full of last session's markers would be
        /// drawn over the new world until something republished. Both fields are assigned
        /// directly rather than through a helper: <c>DomainReloadStaticResetTests</c> reads
        /// this method's raw IL and only recognises a bare <c>stsfld</c> or a
        /// <c>field.Clear()</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _channels.Clear();
            _flat = new List<WorldMarker>();
            _dirty = false;
        }
    }
}
