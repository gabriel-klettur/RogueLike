using System;
using UnityEngine;

namespace Valkur.Core.Coordinates
{
    /// <summary>
    /// A tile-precision position inside a world. Uses <see cref="long"/> for
    /// each axis so the address space outlasts <see cref="float"/> precision —
    /// a Unity <see cref="Vector3"/> position becomes jittery beyond ±16k units,
    /// while <see cref="WorldPos"/> can address ~9.2e18 tiles without wraparound.
    ///
    /// Phase 2 introduces a client-side "active origin" (a <see cref="Vector2Int"/>
    /// rebased whenever the player drifts &gt; 2048 tiles from it). The Sim layer
    /// always reasons in <see cref="WorldPos"/>; only the presentation layer
    /// converts to <see cref="Vector3"/> at render time using <see cref="ToUnity"/>.
    /// </summary>
    [Serializable]
    public readonly struct WorldPos : IEquatable<WorldPos>
    {
        public readonly WorldId World;
        public readonly long Tx;
        public readonly long Ty;

        public WorldPos(WorldId world, long tx, long ty)
        {
            World = world;
            Tx = tx;
            Ty = ty;
        }

        public bool Equals(WorldPos other) => World.Equals(other.World) && Tx == other.Tx && Ty == other.Ty;
        public override bool Equals(object obj) => obj is WorldPos p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = World.GetHashCode();
                h = (h * 397) ^ Tx.GetHashCode();
                h = (h * 397) ^ Ty.GetHashCode();
                return h;
            }
        }

        public override string ToString() => $"{World}@({Tx},{Ty})";

        public static bool operator ==(WorldPos a, WorldPos b) => a.Equals(b);
        public static bool operator !=(WorldPos a, WorldPos b) => !a.Equals(b);

        /// <summary>
        /// Convert to a Unity world-space <see cref="Vector3"/> relative to the
        /// supplied active origin. This is the ONLY legitimate way to render a
        /// <see cref="WorldPos"/> — never cast Tx/Ty directly to float for view code.
        /// </summary>
        public Vector3 ToUnity(Vector2Int activeOrigin, float tileSize)
            => new Vector3((Tx - activeOrigin.x) * tileSize, (Ty - activeOrigin.y) * tileSize, 0f);

        /// <summary>
        /// Compute the chunk this position falls into, given the chunk side length.
        /// Negative tile coordinates floor toward minus infinity (so tile -1 lives
        /// in chunk -1, not chunk 0).
        /// </summary>
        public ChunkCoord ToChunk(int chunkSize)
        {
            long cx = Tx >= 0 ? Tx / chunkSize : -((-Tx + chunkSize - 1) / chunkSize);
            long cy = Ty >= 0 ? Ty / chunkSize : -((-Ty + chunkSize - 1) / chunkSize);
            return new ChunkCoord(World, (int)cx, (int)cy);
        }

        /// <summary>Local tile offset inside its chunk (always 0..chunkSize-1).</summary>
        public Vector2Int LocalInChunk(int chunkSize)
        {
            int lx = (int)(((Tx % chunkSize) + chunkSize) % chunkSize);
            int ly = (int)(((Ty % chunkSize) + chunkSize) % chunkSize);
            return new Vector2Int(lx, ly);
        }
    }
}
