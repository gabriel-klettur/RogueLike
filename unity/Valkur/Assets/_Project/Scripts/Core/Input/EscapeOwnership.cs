using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Who owns Escape right now.
    ///
    /// Escape is the one key with several independent readers: the General Editor opens on
    /// it, and any overlay that closes on it — the Save Log, the character sheet, a confirm
    /// dialog — reads the same press in the same frame. With nothing arbitrating, one press
    /// closed the overlay AND toggled the launcher behind it. <see cref="InputBlocker"/> is
    /// the wrong tool for that: it suppresses every gameplay key, and those overlays are not
    /// modal — the player keeps walking with the character sheet up.
    ///
    /// An overlay claims Escape while it is open and releases it as it closes. A release
    /// stays in force for the REST of the frame it happened on, because Update order between
    /// the overlay and the launcher is undefined: an overlay that closes on the press and
    /// releases inside its own Update would otherwise hand that same press to a launcher
    /// that runs later in the frame.
    /// </summary>
    public static class EscapeOwnership
    {
        private static readonly HashSet<object> _owners = new HashSet<object>();
        private static int _releasedFrame = -1;

        /// <summary>True while any overlay holds Escape, or one released it this frame.</summary>
        public static bool IsClaimed => IsClaimedOn(Time.frameCount);

        /// <summary>How many overlays currently hold a claim. Diagnostic.</summary>
        public static int OwnerCount => _owners.Count;

        /// <summary>
        /// The frame-explicit form of <see cref="IsClaimed"/>, so a test can ask about the
        /// next frame without being able to advance the clock.
        /// </summary>
        public static bool IsClaimedOn(int frameCount)
            => _owners.Count > 0 || _releasedFrame == frameCount;

        /// <summary>Hold Escape. Idempotent per owner.</summary>
        public static void Claim(object owner)
        {
            if (owner == null) return;
            _owners.Add(owner);
        }

        /// <summary>
        /// Let go of Escape. The release takes effect on the NEXT frame — see the class note.
        /// An owner that never claimed is ignored and does not stamp the frame.
        /// </summary>
        public static void Release(object owner)
        {
            if (owner == null || !_owners.Remove(owner)) return;
            _releasedFrame = Time.frameCount;
        }

        /// <summary>Tests only: forget every claim and the release stamp.</summary>
        public static void ResetForTests() => ResetStaticState();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _owners.Clear();
            _releasedFrame = -1;
        }
    }
}
