using System;

namespace Valkur.Core.Coordinates
{
    /// <summary>
    /// Stable identity for a world (a top-level container of chunks). Holds a
    /// <see cref="Guid"/> for DB / persistence keys and a human slug for logs
    /// and asset paths. Two <see cref="WorldId"/> are equal iff their GUIDs match;
    /// the slug is informational only.
    ///
    /// Embryonic: today the entire game lives in a single world identified by
    /// <see cref="Base"/>. Multi-world routing (Phase 1) and chunk streaming
    /// (Phase 2) will key off this struct, so every new persistence-touching
    /// API should already accept it even when the value is always
    /// <see cref="Base"/>. Adding the parameter later is a breaking change
    /// across hundreds of callsites; adding it now is free.
    /// </summary>
    [Serializable]
    public readonly struct WorldId : IEquatable<WorldId>
    {
        public readonly Guid Value;
        public readonly string Slug;

        public WorldId(Guid value, string slug)
        {
            Value = value;
            Slug = slug ?? string.Empty;
        }

        /// <summary>
        /// The default single-player world. While the game is single-world this
        /// is what every legacy callsite resolves to.
        /// </summary>
        public static readonly WorldId Base = new WorldId(Guid.Empty, "base");

        public bool IsEmpty => Value == Guid.Empty && string.IsNullOrEmpty(Slug);

        public bool Equals(WorldId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is WorldId w && Equals(w);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => string.IsNullOrEmpty(Slug) ? Value.ToString("N") : Slug;

        public static bool operator ==(WorldId a, WorldId b) => a.Equals(b);
        public static bool operator !=(WorldId a, WorldId b) => !a.Equals(b);
    }
}
