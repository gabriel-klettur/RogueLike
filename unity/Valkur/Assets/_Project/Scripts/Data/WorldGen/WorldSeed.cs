using System;
using System.Globalization;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Seeds, the way a player types them.
    ///
    /// <para>A number is used as-is; any other text is hashed, so "valkur" is a seed as good as
    /// 1337 and two players who type the same word get the same world. The hash is FNV-1a over
    /// UTF-16 code units — never <c>string.GetHashCode</c>, which .NET is free to vary between
    /// processes and would give a shared seed a different world on every launch.</para>
    /// </summary>
    public static class WorldSeed
    {
        /// <summary>Text or number to a seed. Empty text answers false.</summary>
        public static bool TryParse(string text, out int seed)
        {
            seed = 0;
            if (text == null) return false;
            text = text.Trim();
            if (text.Length == 0) return false;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                return true;

            seed = Hash(text);
            return true;
        }

        public static int Hash(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    h ^= text[i];
                    h *= 16777619u;
                }
                return (int)h;
            }
        }

        /// <summary>
        /// A derived seed for one independent stream (a noise channel, a structure type). Mixing
        /// rather than adding: seed+1 for temperature and seed+2 for humidity would give two
        /// neighbouring seeds overlapping channels.
        /// </summary>
        public static int Derive(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (int)h;
            }
        }

        /// <summary>A fresh seed. Only for the "new seed" button — generation never calls it.</summary>
        public static int NewRandom(System.Random rng) => rng.Next(int.MinValue, int.MaxValue);
    }
}
