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

        /// <summary>
        /// True iff this id refers to the legacy "base" world — either as
        /// <see cref="Base"/> exactly (Guid.Empty + slug "base"), or as a
        /// descriptor-derived id whose slug happens to be "base" (the
        /// canonical legacy slug). Persistence repositories use this to
        /// keep WorldId.Base on the byte-compatible flat StreamingAssets
        /// layout regardless of how the id was constructed.
        ///
        /// Without this, a designer-authored "base" descriptor would
        /// produce a deterministic Guid distinct from Guid.Empty, and
        /// repos would silently start writing into Worlds/base/ instead
        /// of the legacy root — breaking single-world byte-compat.
        /// </summary>
        public bool IsBase => string.Equals(Slug, "base", StringComparison.OrdinalIgnoreCase) || IsEmpty;

        public bool Equals(WorldId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is WorldId w && Equals(w);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => string.IsNullOrEmpty(Slug) ? Value.ToString("N") : Slug;

        public static bool operator ==(WorldId a, WorldId b) => a.Equals(b);
        public static bool operator !=(WorldId a, WorldId b) => !a.Equals(b);
    }
}
