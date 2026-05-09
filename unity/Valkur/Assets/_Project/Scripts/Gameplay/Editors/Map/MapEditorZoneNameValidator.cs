using System;
using System.Collections.Generic;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Pre-flight validation for user-supplied zone names. Centralised so
    /// every CRUD path (Add Zone dialog, inline rename, duplicate suggestion,
    /// future bulk-rename tooling) returns the same rejection reasons in the
    /// same wording — and so the rules live in one file when designers want
    /// to revisit them.
    ///
    /// All checks are case-insensitive (zoneManager keys are
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>) so "Forest" colliding
    /// with "forest" is reported as a duplicate, not allowed through.
    /// </summary>
    public static class MapEditorZoneNameValidator
    {
        public const int MinLength = 1;
        public const int MaxLength = 64;

        public enum Result
        {
            Ok,
            Empty,
            TooLong,
            InvalidCharacters,
            DuplicateInSlot,
        }

        /// <summary>
        /// Validate <paramref name="rawName"/> against the live
        /// <paramref name="zoneManager"/>. <paramref name="excludeName"/> lets
        /// callers skip the duplicate check for one specific name (used by
        /// rename — the zone keeps its old slot in the map until rename
        /// commits, so checking the new name against itself would always
        /// fail). Returns a structured reason + a human message ready for
        /// the status bar.
        /// </summary>
        public static Result Validate(string rawName, ZoneManager zoneManager,
            string excludeName, out string trimmed, out string reasonMessage)
        {
            trimmed       = (rawName ?? string.Empty).Trim();
            reasonMessage = string.Empty;

            if (trimmed.Length < MinLength)
            {
                reasonMessage = "Zone name cannot be empty.";
                return Result.Empty;
            }
            if (trimmed.Length > MaxLength)
            {
                reasonMessage = $"Zone name is too long ({trimmed.Length} chars; max {MaxLength}).";
                return Result.TooLong;
            }
            if (!HasOnlySafeCharacters(trimmed))
            {
                reasonMessage = "Zone name may only contain letters, digits, '_', '-' and spaces.";
                return Result.InvalidCharacters;
            }

            if (zoneManager != null)
            {
                bool collidesWithExisting =
                    zoneManager.TryGetZone(trimmed, out _) &&
                    !string.Equals(trimmed, excludeName, StringComparison.OrdinalIgnoreCase);
                if (collidesWithExisting)
                {
                    reasonMessage = $"A zone named '{trimmed}' already exists in this map.";
                    return Result.DuplicateInSlot;
                }
            }

            return Result.Ok;
        }

        private static bool HasOnlySafeCharacters(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c)) continue;
                if (c == '_' || c == '-' || c == ' ') continue;
                return false;
            }
            return true;
        }
    }
}
