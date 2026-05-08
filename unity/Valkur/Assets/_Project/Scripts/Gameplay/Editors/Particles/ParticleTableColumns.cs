using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    // ── Column category (drives the coloured stripe atop each header cell) ────────

    /// <summary>
    /// Logical group every <see cref="ParticleTableColumn"/> belongs to. Used by the
    /// table header to draw a thin coloured stripe over each cell.
    /// </summary>
    internal enum ParticleColumnCategory
    {
        Identity,
        VFX
    }

    // ── Editor kind enum ──────────────────────────────────────────────────────────

    internal enum ParticleTableEditorKind
    {
        Text,
        Int,
        Float,
        Toggle
    }

    // ── Column descriptor ─────────────────────────────────────────────────────────

    /// <summary>
    /// Describes how a single <see cref="ParticlePresetDefinition"/> field is displayed
    /// and edited in the Presets table view. Mirrors <c>SpellTableColumn</c> — only the
    /// generic type parameter differs.
    /// </summary>
    internal sealed class ParticleTableColumn
    {
        public string                             Header     { get; }
        public float                              Width      { get; }
        public ParticleTableEditorKind            EditorKind { get; }
        public ParticleColumnCategory             Category   { get; }
        public string                             Tooltip    { get; }
        public Func<ParticlePresetDefinition, string>   GetString { get; }
        public Action<ParticlePresetDefinition, string> SetString { get; }

        public ParticleTableColumn(string header, float width, ParticleTableEditorKind kind,
            ParticleColumnCategory category, string tooltip,
            Func<ParticlePresetDefinition, string> getString,
            Action<ParticlePresetDefinition, string> setString = null)
        {
            Header     = header;
            Width      = width;
            EditorKind = kind;
            Category   = category;
            Tooltip    = tooltip;
            GetString  = getString;
            SetString  = setString;
        }
    }

    // ── Column registry ───────────────────────────────────────────────────────────

    /// <summary>
    /// Static registry of every <see cref="ParticleTableColumn"/> in left-to-right
    /// display order. Adding a new <see cref="ParticlePresetDefinition"/> field requires
    /// only a single new entry here.
    /// </summary>
    internal static class ParticleTableColumns
    {
        // Column widths
        private const float W_ID      = 140f;
        private const float W_NAME    = 140f;
        private const float W_TYPE    = 90f;
        private const float W_KIND    = 90f;
        private const float W_FLOAT   = 72f;
        private const float W_INT     = 64f;
        private const float W_BOOL    = 64f;

        private static readonly IReadOnlyList<ParticleTableColumn> _columns = BuildRegistry();
        public static IReadOnlyList<ParticleTableColumn> All => _columns;

        // ── Category palette ──────────────────────────────────────────────────

        private static readonly Color C_IDENTITY = new Color(0.78f, 0.78f, 0.82f, 1f); // light grey
        private static readonly Color C_VFX      = new Color(0.80f, 0.55f, 0.95f, 1f); // lavender

        public static Color CategoryColor(ParticleColumnCategory cat)
        {
            switch (cat)
            {
                case ParticleColumnCategory.Identity: return C_IDENTITY;
                case ParticleColumnCategory.VFX:      return C_VFX;
                default:                              return C_IDENTITY;
            }
        }

        // ── Default hidden (no defaults; all columns visible on first open) ───

        public static readonly HashSet<string> DefaultHidden
            = new HashSet<string>(StringComparer.Ordinal);

        // ── Registry ──────────────────────────────────────────────────────────

        private static List<ParticleTableColumn> BuildRegistry()
        {
            return new List<ParticleTableColumn>
            {
                // ── Identity ──────────────────────────────────────────────────
                ColText("id", W_ID, ParticleColumnCategory.Identity,
                    "Unique key used to reference this preset from spell definitions.",
                    d => d.id ?? ""),
                    // read-only: identity key — no setter

                ColText("name", W_NAME, ParticleColumnCategory.Identity,
                    "Human-readable display name shown in the editor and tooltip.",
                    d => d.displayName ?? "",
                    (d, v) => d.displayName = v),

                ColText("type", W_TYPE, ParticleColumnCategory.Identity,
                    "Category string (e.g. 'aura', 'dash', 'explosion').",
                    d => d.type ?? "",
                    (d, v) => d.type = v),

                // ── VFX ───────────────────────────────────────────────────────
                ColText("kind", W_KIND, ParticleColumnCategory.VFX,
                    "VFX kind: aura, dash, laser, lightning, slash, explosion, smoke…",
                    d => d.vfx?.kind ?? "",
                    (d, v) => { if (d.vfx != null) d.vfx.kind = v; }),

                ColFloat("emitRate", W_FLOAT, ParticleColumnCategory.VFX,
                    "Particles emitted per second (looping emitters).",
                    d => d.vfx?.emitRate ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.emitRate = v; }),

                ColInt("burstCount", W_INT, ParticleColumnCategory.VFX,
                    "Particle count for burst/one-shot emitters.",
                    d => d.vfx?.count ?? 0,
                    (d, v) => { if (d.vfx != null) d.vfx.count = v; }),

                ColFloat("lifespan", W_FLOAT, ParticleColumnCategory.VFX,
                    "Particle lifetime in seconds.",
                    d => d.vfx?.lifespan ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.lifespan = v; }),

                ColFloat("speed", W_FLOAT, ParticleColumnCategory.VFX,
                    "Initial particle speed (world units / s).",
                    d => d.vfx?.speed ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.speed = v; }),

                ColFloat("gravity", W_FLOAT, ParticleColumnCategory.VFX,
                    "Gravity acceleration (world units / s²). Positive = down.",
                    d => d.vfx?.gravity ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.gravity = v; }),

                ColFloat("drag", W_FLOAT, ParticleColumnCategory.VFX,
                    "Velocity damping factor [0..1] applied per second.",
                    d => d.vfx?.drag ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.drag = v; }),

                ColFloat("sizeMin", W_FLOAT, ParticleColumnCategory.VFX,
                    "Minimum particle size (world units).",
                    d => d.vfx?.sizeMin ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.sizeMin = v; }),

                ColFloat("sizeMax", W_FLOAT, ParticleColumnCategory.VFX,
                    "Maximum particle size (world units).",
                    d => d.vfx?.sizeMax ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.sizeMax = v; }),

                ColFloat("radius", W_FLOAT, ParticleColumnCategory.VFX,
                    "Emission radius for aura/circle shapes (world units).",
                    d => d.vfx?.radius ?? 0f,
                    (d, v) => { if (d.vfx != null) d.vfx.radius = v; }),

                ColBool("loops", W_BOOL, ParticleColumnCategory.VFX,
                    "When true the emitter runs continuously; false = one-shot burst.",
                    d => d.vfx?.loops ?? false,
                    (d, v) => { if (d.vfx != null) d.vfx.loops = v; }),

                ColBool("additive", W_BOOL, ParticleColumnCategory.VFX,
                    "Use additive blending (bloom / glow effects).",
                    d => d.vfx?.additive ?? false,
                    (d, v) => { if (d.vfx != null) d.vfx.additive = v; }),
            };
        }

        // ── Convenience factories ─────────────────────────────────────────────

        private static ParticleTableColumn ColText(string header, float width,
            ParticleColumnCategory cat, string tip,
            Func<ParticlePresetDefinition, string> get,
            Action<ParticlePresetDefinition, string> set = null)
            => new ParticleTableColumn(header, width, ParticleTableEditorKind.Text, cat, tip, get, set);

        private static ParticleTableColumn ColInt(string header, float width,
            ParticleColumnCategory cat, string tip,
            Func<ParticlePresetDefinition, int> getInt,
            Action<ParticlePresetDefinition, int> setInt)
            => new ParticleTableColumn(header, width, ParticleTableEditorKind.Int, cat, tip,
                d => getInt(d).ToString(),
                (d, v) => { if (int.TryParse(v, out var i)) setInt(d, i); });

        private static ParticleTableColumn ColFloat(string header, float width,
            ParticleColumnCategory cat, string tip,
            Func<ParticlePresetDefinition, float> getF,
            Action<ParticlePresetDefinition, float> setF)
            => new ParticleTableColumn(header, width, ParticleTableEditorKind.Float, cat, tip,
                d => getF(d).ToString("0.###"),
                (d, v) =>
                {
                    if (float.TryParse(v,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var f))
                        setF(d, f);
                });

        private static ParticleTableColumn ColBool(string header, float width,
            ParticleColumnCategory cat, string tip,
            Func<ParticlePresetDefinition, bool> getB,
            Action<ParticlePresetDefinition, bool> setB)
            => new ParticleTableColumn(header, width, ParticleTableEditorKind.Toggle, cat, tip,
                d => getB(d).ToString(),
                (d, v) => { if (bool.TryParse(v, out var b)) setB(d, b); });
    }
}
