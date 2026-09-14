using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.WorldGen;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>The three parameter tabs and the field rows they are built from.</summary>
    public partial class SeedWorldRuntimeEditor
    {
        /// <summary>
        /// One per field: re-reads the settings and writes the box WITHOUT notifying. Rebuilding
        /// the body on every commit would destroy the field the author just tabbed into
        /// (<c>UIInputField.AddCommit</c> fires on focus loss too), and re-syncing shows a
        /// CLAMPED value at once. Same lesson the Death, Skills and Economy editors paid for.
        /// </summary>
        private readonly List<Action> _fieldResync = new List<Action>();

        private void ResyncFields()
        {
            for (int i = 0; i < _fieldResync.Count; i++) _fieldResync[i]?.Invoke();
        }

        // ── Tabs ───────────────────────────────────────────────────────────────

        private void BuildWorldTab(Transform body)
        {
            EditorUIHelpers.BuildSectionHeader(body, "Semilla");

            var seedRow = MakeRow(body, "SeedRow", ROW_H + 4f);
            AddCaption(seedRow.transform, "Semilla");
            var seedField = EditorUIHelpers.AddInputField(seedRow.transform,
                _settings.seed.ToString(CultureInfo.InvariantCulture), SetSeedText);
            seedField.gameObject.name = "SeedField";
            var dice = EditorUIHelpers.MakeButton(seedRow.transform, "Aleatoria", RandomSeed, ROW_H, 11f);
            var diceLe = dice.gameObject.AddComponent<LayoutElement>();
            diceLe.preferredWidth = 80f;
            diceLe.flexibleWidth = 0f;
            _fieldResync.Add(() => seedField.SetTextWithoutNotify(_settings.seed.ToString(CultureInfo.InvariantCulture)));
            AddHint(body, "Un numero, o cualquier palabra: la misma palabra da siempre el mismo mundo.");

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Tamano");

            AddIntField(body, "Ancho (tiles)", null,
                () => _settings.widthTiles, v => _settings.widthTiles = v,
                WorldGenSettings.MinSizeTiles, WorldGenSettings.MaxWidthTiles);
            AddIntField(body, "Alto (tiles)",
                $"Maximo {WorldGenSettings.MaxHeightTiles}: el orden de dibujado (Y-sort) no cabe en mas " +
                "sin desplazar el origen del mundo.",
                () => _settings.heightTiles, v => _settings.heightTiles = v,
                WorldGenSettings.MinSizeTiles, WorldGenSettings.MaxHeightTiles);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Continentes");

            AddFloatField(body, "Escala", "Tamano tipico de un continente, en tiles.",
                () => _settings.continentScale, v => _settings.continentScale = v, 20f, 1000f);
            AddIntField(body, "Detalle (octavas)", "Mas octavas, costas mas irregulares.",
                () => _settings.detailOctaves, v => _settings.detailOctaves = v, 1, 8);
            AddFloatField(body, "Nivel del mar", "0 a 1. Subirlo inunda el mundo.",
                () => _settings.seaLevel, v => _settings.seaLevel = v, 0f, 1f);
            AddFloatField(body, "Linea de montana", "Por encima de esta altura, montana (o volcan si hace calor).",
                () => _settings.mountainLevel, v => _settings.mountainLevel = v, 0f, 1f);
            AddFloatField(body, "Borde oceanico", "Cuanto se hunden los bordes. 0 = el mundo termina en un corte.",
                () => _settings.edgeFalloff, v => _settings.edgeFalloff = v, 0f, 1f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Rios");

            AddIntField(body, "Rios", "Cuantos intenta trazar desde las tierras altas hasta el mar.",
                () => _settings.riverCount, v => _settings.riverCount = v, 0, WorldGenSettings.MaxRivers);
            AddIntField(body, "Anchura (tiles)", null,
                () => _settings.riverWidth, v => _settings.riverWidth = v, 1, WorldGenSettings.MaxRiverWidth);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Pueblos");

            AddIntField(body, "Pueblos", "Cuantos intenta fundar, el inicial incluido. Solo en tierra llana y sin rios en el centro.",
                () => _settings.townCount, v => _settings.townCount = v, 0, WorldGenSettings.MaxTowns);
            AddIntField(body, "Radio (tiles)", "Hasta donde llegan sus calles principales desde la plaza.",
                () => _settings.townRadius, v => _settings.townRadius = v,
                WorldGenSettings.MinTownRadius, WorldGenSettings.MaxTownRadius);

            var startRow = MakeRow(body, "StartTownRow", ROW_H + 4f);
            AddCaption(startRow.transform, "Pueblo inicial");
            bool start = _settings.startingTown;
            var startToggle = EditorUIHelpers.MakeButton(startRow.transform, start ? "SI" : "NO",
                () => { Commit($"Pueblo inicial: {(start ? "no" : "si")}", s => s.startingTown = !start); RebuildBody(); },
                ROW_H, 11f);
            startToggle.gameObject.name = "StartingTownToggle";
            UIButton.SetTint(startToggle, start ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL);
            AddHint(body, "Con el pueblo inicial la partida empieza en su calle principal, no en mitad del campo.");

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Poblacion");

            AddIntField(body, "Encuentros", "Campamentos hostiles fuera de los pueblos. Mas lejos del inicio, mas peligrosos.",
                () => _settings.encounterCount, v => _settings.encounterCount = v, 0, WorldGenSettings.MaxEncounters);
            AddFloatField(body, "Densidad de arboles", "0 = sin arboles. La familia la decide el bioma (taiga: invierno; selva: tropical).",
                () => _settings.treeDensity, v => _settings.treeDensity = v, 0f, WorldGenSettings.MaxTreeDensity);
            AddHint(body, "Los vendedores viven en el pueblo inicial: cada uno es un personaje unico.");

            EditorUIHelpers.BuildSeparator(body);
            BuildBakeSection(body);
        }

        private void BuildClimateTab(Transform body)
        {
            EditorUIHelpers.BuildSectionHeader(body, "Clima");

            AddFloatField(body, "Escala del clima", "Tamano tipico de una region climatica, en tiles.",
                () => _settings.climateScale, v => _settings.climateScale = v, 30f, 1500f);
            AddFloatField(body, "Temperatura", "-0.5 a 0.5. Desplaza todo el mundo hacia el calor o el frio.",
                () => _settings.temperatureBias, v => _settings.temperatureBias = v, -0.5f, 0.5f);
            AddFloatField(body, "Humedad", "-0.5 a 0.5. Desplaza todo el mundo hacia la selva o el desierto.",
                () => _settings.humidityBias, v => _settings.humidityBias = v, -0.5f, 0.5f);
            AddFloatField(body, "Latitud", "Cuanto mas frio es el norte que el sur. 0 = sin latitud.",
                () => _settings.latitude, v => _settings.latitude = v, 0f, 1f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Rareza");

            AddFloatField(body, "Biomas raros", "Cuanta tierra pueden reclamar Encantado y Corrupto.",
                () => _settings.rarity, v => _settings.rarity = v, 0f, 1f);

            AddHint(body, "La altura enfria: una montana es mas fria que la costa a su lado.");
        }

        private void BuildBiomesTab(Transform body)
        {
            EditorUIHelpers.BuildSectionHeader(body, "Biomas");
            AddHint(body, "Desactivar un bioma reparte su espacio entre sus vecinos de clima. El peso " +
                          "es cuanto espacio reclama frente a ellos (1 = neutro).");

            for (int i = 0; i < WorldBiomeTable.Count; i++)
                BuildBiomeRow(body, WorldBiomeTable.GetAt(i));
        }

        private void BuildBiomeRow(Transform body, WorldBiomeInfo info)
        {
            var biome = info.Biome;
            var weight = _settings.WeightOf(biome);

            var row = MakeRow(body, "Biome_" + biome, ROW_H + 2f);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = false;

            var swatch = EditorUIHelpers.CreateUI("Swatch", row.transform);
            var swatchImage = swatch.AddComponent<Image>();
            swatchImage.color = info.PreviewColor;
            swatchImage.raycastTarget = false;
            var swatchLe = swatch.AddComponent<LayoutElement>();
            swatchLe.preferredWidth = 16f;
            swatchLe.flexibleWidth = 0f;

            var name = EditorUIHelpers.AddLabel(row.transform, info.DisplayName, 11f);
            name.color = weight.enabled ? EditorUIHelpers.TEXT_PRIMARY : EditorUIHelpers.TEXT_MUTED;
            var nameLe = name.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredWidth = 110f;
            nameLe.flexibleWidth = 1f;

            bool enabled = weight.enabled;
            var toggle = EditorUIHelpers.MakeButton(row.transform, enabled ? "SI" : "NO",
                () => SetBiomeEnabled(biome, !enabled), ROW_H, 11f);
            toggle.gameObject.name = "Toggle_" + biome;
            UIButton.SetTint(toggle, enabled ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL);
            var toggleLe = toggle.gameObject.AddComponent<LayoutElement>();
            toggleLe.preferredWidth = 44f;
            toggleLe.flexibleWidth = 0f;

            // Only the Land family competes in the climate plane; a weight on water, shore,
            // highland or a rare biome would be a control that moves nothing.
            if (info.Kind != WorldBiomeKind.Land)
            {
                var kind = EditorUIHelpers.AddLabel(row.transform, KindLabel(info), 10f);
                kind.color = EditorUIHelpers.TEXT_MUTED;
                var kindLe = kind.gameObject.AddComponent<LayoutElement>();
                kindLe.preferredWidth = 70f;
                kindLe.flexibleWidth = 0f;
                return;
            }

            var field = EditorUIHelpers.AddInputField(row.transform, Fmt(weight.weight), text =>
            {
                if (!TryParseFloat(text, out float v)) { ResyncFields(); return; }
                string label = $"{info.DisplayName}: peso {Fmt(v)}";
                Commit(label, s => s.WeightOf(biome).weight = v);
            });
            field.gameObject.name = "Weight_" + biome;
            // Never `??` on a Unity object: a destroyed component is == null but not C# null.
            var fieldLe = field.gameObject.GetComponent<LayoutElement>();
            if (fieldLe == null) fieldLe = field.gameObject.AddComponent<LayoutElement>();
            fieldLe.preferredWidth = 70f;
            fieldLe.flexibleWidth = 0f;
            _fieldResync.Add(() => field.SetTextWithoutNotify(Fmt(_settings.WeightOf(biome).weight)));
        }

        private static string KindLabel(WorldBiomeInfo info)
        {
            switch (info.Kind)
            {
                case WorldBiomeKind.Water:    return info.Biome == WorldBiome.River ? "trazado" : "por altura";
                case WorldBiomeKind.Shore:    return "por altura";
                case WorldBiomeKind.Highland: return "por altura";
                case WorldBiomeKind.Rare:     return "por rareza";
                default:                      return string.Empty;
            }
        }

        // ── Field rows ─────────────────────────────────────────────────────────

        private void AddFloatField(Transform parent, string label, string hint,
                                   Func<float> get, Action<float> set, float min, float max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            AddCaption(row.transform, label);

            var field = EditorUIHelpers.AddInputField(row.transform, Fmt(get()), text =>
            {
                if (!TryParseFloat(text, out float v)) { ResyncFields(); return; }
                float clamped = Mathf.Clamp(v, min, max);
                Commit($"{label}: {Fmt(clamped)}", _ => set(clamped));
            });

            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(() => field.SetTextWithoutNotify(Fmt(get())));
        }

        private void AddIntField(Transform parent, string label, string hint,
                                 Func<int> get, Action<int> set, int min, int max)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            AddCaption(row.transform, label);

            var field = EditorUIHelpers.AddInputField(row.transform,
                get().ToString(CultureInfo.InvariantCulture), text =>
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                { ResyncFields(); return; }
                int clamped = Mathf.Clamp(v, min, max);
                Commit($"{label}: {clamped}", _ => set(clamped));
            });

            if (!string.IsNullOrEmpty(hint)) AddHint(parent, hint);
            _fieldResync.Add(() => field.SetTextWithoutNotify(get().ToString(CultureInfo.InvariantCulture)));
        }

        private static void AddCaption(Transform row, string label)
        {
            var caption = EditorUIHelpers.AddLabel(row, label, 11f);
            var element = caption.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = LABEL_W;
            element.flexibleWidth = 0f;
        }

        private static void AddHint(Transform parent, string text)
        {
            var hint = EditorUIHelpers.AddLabel(parent, text, 9.5f);
            hint.color = EditorUIHelpers.TEXT_MUTED;
            hint.enableWordWrapping = true;
            var element = hint.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 26f;
            element.minHeight = 14f;
            element.flexibleHeight = 0f;
        }

        /// <summary>Accepts a comma as well as a point, because a Spanish keyboard types one.</summary>
        private static bool TryParseFloat(string text, out float value)
            => float.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Float,
                              CultureInfo.InvariantCulture, out value);

        private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
