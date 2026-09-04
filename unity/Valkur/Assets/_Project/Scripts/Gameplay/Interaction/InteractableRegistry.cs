using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// Every live <see cref="IPlayerInteractable"/>, and the "what is nearest to the player"
    /// query the interaction controller runs once a frame.
    ///
    /// <para>A registry rather than a physics query for the same reason
    /// <c>DestructibleObstacleRegistry</c> is one: an <c>OverlapCircleAll</c> against
    /// Building would walk every painted collision cell and every building box in range,
    /// every frame, to find the handful of things that are actually workable. It also keeps
    /// the answer exact — a mine and the hillside behind it share a layer, and only one of
    /// them is a seam.</para>
    ///
    /// <para>SCALE. A forest is hundreds of interactables, so above
    /// <see cref="HASH_THRESHOLD"/> the search goes through a <see cref="SpatialHash{T}"/>.
    /// Below it the flat scan is kept: building a hash to search six items costs more than
    /// searching six items. Interactables do not move, so the hash is rebuilt only when
    /// membership changes.</para>
    /// </summary>
    public static class InteractableRegistry
    {
        /// <summary>
        /// Membership count at which the spatial hash starts paying for itself. Matches
        /// <c>DestructibleObstacleRegistry</c>'s, since the two hold overlapping populations.
        /// </summary>
        private const int HASH_THRESHOLD = 24;

        private static List<IPlayerInteractable> _live = new List<IPlayerInteractable>(8);

        /// <summary>
        /// Interactables whose BOUNDS MOVE, held apart from the hash and scanned linearly by
        /// both traversals.
        ///
        /// <para>The hash indexes an entry by the position it held when the hash was last
        /// REBUILT, and it rebuilds only when membership changes. A stationary building is
        /// therefore indexed correctly forever; a fishing spot that follows the nearest water
        /// cell would be looked up at wherever it happened to be when some unrelated node
        /// registered, and would simply stop being retrieved as the player walked. That fails
        /// only above <see cref="HASH_THRESHOLD"/> — so it works in an empty test scene and
        /// breaks in the shipped world, which is measured at 88 nodes with 87 registered.</para>
        ///
        /// <para>A separate list rather than dirtying the hash on every move: re-registering
        /// would rebuild all ninety entries every time the player crossed a cell boundary,
        /// several times a second, to move one. This list is normally empty and costs the
        /// hashed path one loop over nothing.</para>
        /// </summary>
        private static List<IPlayerInteractable> _dynamic = new List<IPlayerInteractable>(2);

        // Hoisted rather than allocated per query: this runs every frame, forever.
        private static SpatialHash<IPlayerInteractable> _hash =
            new SpatialHash<IPlayerInteractable>(4f);
        private static List<(IPlayerInteractable item, Vector2 pos)> _queryBuffer =
            new List<(IPlayerInteractable, Vector2)>(32);
        private static bool _hashDirty = true;

        /// <summary>
        /// Widest (radius + half-diagonal) any registered interactable reaches. The hash
        /// indexes by a POINT while the range test is against BOUNDS, so a query has to be
        /// widened by this or a mine face the player is standing against would never be
        /// retrieved as a candidate.
        /// </summary>
        private static float _maxReach;

        /// <summary>Everything the query can reach, fixed and moving alike.</summary>
        public static int Count => _live.Count + _dynamic.Count;

        /// <summary>How many of those have moving bounds. Normally zero.</summary>
        public static int DynamicCount => _dynamic.Count;

        /// <summary>
        /// Domain Reload is OFF. A list left holding destroyed interactables would carry into
        /// the next Play session and be walked by the first frame. Assigning FRESH containers
        /// is a plain <c>stsfld</c>, the only reset shape <c>DomainReloadStaticResetTests</c>
        /// recognises — <c>_live.Clear()</c> passes the field as an argument and reads to that
        /// scanner as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _live = new List<IPlayerInteractable>(8);
            _dynamic = new List<IPlayerInteractable>(2);
            _hash = new SpatialHash<IPlayerInteractable>(4f);
            _queryBuffer = new List<(IPlayerInteractable, Vector2)>(32);
            _hashDirty = true;
            _maxReach = 0f;
        }

        public static void Register(IPlayerInteractable interactable)
        {
            if (interactable == null || _live.Contains(interactable)) return;
            _live.Add(interactable);
            _hashDirty = true;
        }

        /// <summary>
        /// Register something whose <see cref="IPlayerInteractable.InteractionBounds"/> move
        /// between frames. It is asked for its bounds fresh on every query instead of being
        /// indexed by a cached position.
        /// </summary>
        public static void RegisterDynamic(IPlayerInteractable interactable)
        {
            if (interactable == null || _dynamic.Contains(interactable)) return;
            _dynamic.Add(interactable);
        }

        /// <summary>Removes from whichever list holds it, so a caller needs only one exit.</summary>
        public static void Unregister(IPlayerInteractable interactable)
        {
            if (interactable == null) return;
            if (_live.Remove(interactable)) _hashDirty = true;
            _dynamic.Remove(interactable);
        }

        /// <summary>
        /// The interactable whose surface the player is closest to and inside the range of,
        /// or null. Distance is measured to <see cref="IPlayerInteractable.InteractionBounds"/>
        /// so a long mine face competes fairly with a narrow sapling standing beside it —
        /// comparing pivots would hand the prompt to whichever pivot happened to be nearer,
        /// which for a wide building is somewhere inside the hill.
        /// </summary>
        public static IPlayerInteractable FindBest(GameObject player, Vector2 playerPosition)
        {
            if (_live.Count == 0 && _dynamic.Count == 0) return null;

            return _live.Count < HASH_THRESHOLD
                ? FindBestLinear(player, playerPosition)
                : FindBestHashed(player, playerPosition);
        }

        private static IPlayerInteractable FindBestLinear(GameObject player, Vector2 playerPosition)
        {
            IPlayerInteractable best = null;
            float bestDistanceSq = float.MaxValue;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var candidate = _live[i];
                if (candidate == null) { _live.RemoveAt(i); _hashDirty = true; continue; }
                Consider(candidate, player, playerPosition, ref best, ref bestDistanceSq);
            }
            ConsiderDynamic(player, playerPosition, ref best, ref bestDistanceSq);
            return best;
        }

        private static IPlayerInteractable FindBestHashed(GameObject player, Vector2 playerPosition)
        {
            RebuildHashIfDirty();
            _hash.QueryRadius(playerPosition, _maxReach, _queryBuffer);

            IPlayerInteractable best = null;
            float bestDistanceSq = float.MaxValue;

            for (int i = 0; i < _queryBuffer.Count; i++)
            {
                var candidate = _queryBuffer[i].item;
                if (candidate == null) { _hashDirty = true; continue; }
                Consider(candidate, player, playerPosition, ref best, ref bestDistanceSq);
            }
            ConsiderDynamic(player, playerPosition, ref best, ref bestDistanceSq);
            return best;
        }

        /// <summary>
        /// The interactable the player is POINTING AT, or null.
        ///
        /// <para>Proximity cannot express "that one". Standing on a shore with four shoals in
        /// range, or in a forest, the nearest thing and the meant thing are routinely
        /// different — so a pointing gesture needs a query keyed on a world point rather than
        /// on distance.</para>
        ///
        /// <para>SMALLEST AREA WINS among the boxes containing the point. Overlap is the
        /// normal case, not the edge: a shoal drawn inside a bay, a crystal on the face of a
        /// mine. Picking the largest, or the first found, would make the containing thing
        /// unclickable-through and the contained thing unreachable.</para>
        ///
        /// <para>Still range-gated, deliberately. A point query that returned things across
        /// the map would let a gesture claim a click aimed at something the player cannot
        /// reach anyway, and whatever that click WOULD have done is lost for nothing.</para>
        /// </summary>
        public static IPlayerInteractable FindAt(GameObject player, Vector2 worldPoint,
            Vector2 playerPosition)
        {
            IPlayerInteractable best = null;
            float bestArea = float.MaxValue;

            ConsiderAt(_live, player, worldPoint, playerPosition, ref best, ref bestArea);
            ConsiderAt(_dynamic, player, worldPoint, playerPosition, ref best, ref bestArea);
            return best;
        }

        private static void ConsiderAt(List<IPlayerInteractable> pool, GameObject player,
            Vector2 worldPoint, Vector2 playerPosition,
            ref IPlayerInteractable best, ref float bestArea)
        {
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var candidate = pool[i];
                if (candidate == null) { pool.RemoveAt(i); _hashDirty = true; continue; }

                var bounds = candidate.InteractionBounds;
                if (!bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, bounds.center.z)))
                    continue;

                // The same range rule FindBest applies, so a pointed target and a nearest
                // target can never disagree about what "reachable" means.
                Vector2 surface = bounds.ClosestPoint(playerPosition);
                float radius = candidate.InteractionRadius;
                if ((surface - playerPosition).sqrMagnitude > radius * radius) continue;

                if (!candidate.DescribePrompt(player).IsVisible) continue;

                float area = bounds.size.x * bounds.size.y;
                if (area >= bestArea) continue;

                bestArea = area;
                best = candidate;
            }
        }

        /// <summary>Whether this interactable is still known to the registry.</summary>
        public static bool Contains(IPlayerInteractable interactable) =>
            interactable != null && (_live.Contains(interactable) || _dynamic.Contains(interactable));

        /// <summary>
        /// The moving entries, asked for their bounds fresh. Runs in BOTH traversals, which is
        /// the whole point: the hashed path is the one that would otherwise lose them.
        /// </summary>
        private static void ConsiderDynamic(GameObject player, Vector2 playerPosition,
            ref IPlayerInteractable best, ref float bestDistanceSq)
        {
            for (int i = _dynamic.Count - 1; i >= 0; i--)
            {
                var candidate = _dynamic[i];
                if (candidate == null) { _dynamic.RemoveAt(i); continue; }
                Consider(candidate, player, playerPosition, ref best, ref bestDistanceSq);
            }
        }

        /// <summary>
        /// The range test, shared by both traversals so they can never disagree about what
        /// counts as being in range.
        /// </summary>
        private static void Consider(IPlayerInteractable candidate, GameObject player,
            Vector2 playerPosition, ref IPlayerInteractable best, ref float bestDistanceSq)
        {
            // Geometry FIRST, and deliberately. DescribePrompt resolves the player's best tool
            // against this material and formats a countdown; that is cheap once and wasteful
            // for every tree in a forest. The range test is two subtractions and rejects
            // almost everything.
            Vector2 surface = candidate.InteractionBounds.ClosestPoint(playerPosition);
            float distanceSq = (surface - playerPosition).sqrMagnitude;
            float radius = candidate.InteractionRadius;

            if (distanceSq > radius * radius) return;
            if (distanceSq >= bestDistanceSq) return;

            // A target that is REFUSED still competes, as long as it has something to say. A
            // spent seam has to be able to win the prompt in order to tell the player it is
            // spent; hiding it would leave them standing at a rock that looks decorative.
            if (!candidate.DescribePrompt(player).IsVisible) return;

            bestDistanceSq = distanceSq;
            best = candidate;
        }

        private static void RebuildHashIfDirty()
        {
            if (!_hashDirty) return;

            _hash.Clear();
            _maxReach = 0f;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var candidate = _live[i];
                if (candidate == null) { _live.RemoveAt(i); continue; }

                var bounds = candidate.InteractionBounds;
                _hash.Insert(candidate, candidate.InteractionPosition);

                float reach = candidate.InteractionRadius + bounds.extents.magnitude;
                if (reach > _maxReach) _maxReach = reach;
            }

            _hashDirty = false;
        }
    }
}
