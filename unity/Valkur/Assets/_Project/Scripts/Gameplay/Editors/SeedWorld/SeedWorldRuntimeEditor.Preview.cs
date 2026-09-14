using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.WorldGen;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// The preview: a point-filtered texture with one texel per <see cref="WorldGenMap"/> cell,
    /// the layer switcher, the statistics line, the legend and the readout of the cell under the
    /// pointer.
    /// </summary>
    public partial class SeedWorldRuntimeEditor
    {
        private const float PREVIEW_FRAME_H = 430f;
        private const int SPAWN_MARK_RADIUS = 3;

        private RawImage _previewImage;
        private AspectRatioFitter _previewFitter;
        private Texture2D _previewTexture;
        private Color32[] _pixels;

        private TextMeshProUGUI _statsLabel;
        private TextMeshProUGUI _hoverLabel;
        private Transform _legendRoot;

        private readonly List<Button> _layerButtons = new List<Button>();

        internal Texture2D PreviewTexture => _previewTexture;

        private void BuildPreviewPanel()
        {
            var layers = MakeRow(_previewContent, "LayerRow", 26f);
            _layerButtons.Clear();
            AddLayerButton(layers.transform, "Biomas", PreviewLayer.Biome);
            AddLayerButton(layers.transform, "Altura", PreviewLayer.Elevation);
            AddLayerButton(layers.transform, "Temperatura", PreviewLayer.Temperature);
            AddLayerButton(layers.transform, "Humedad", PreviewLayer.Humidity);
            AddLayerButton(layers.transform, "Rareza", PreviewLayer.Rarity);

            var frame = EditorUIHelpers.CreateUI("PreviewFrame", _previewContent);
            var frameLe = frame.AddComponent<LayoutElement>();
            frameLe.preferredHeight = PREVIEW_FRAME_H;
            frameLe.minHeight = PREVIEW_FRAME_H;
            frameLe.flexibleHeight = 0f;

            // The image is a CHILD of the frame, not a layout child: an AspectRatioFitter under a
            // layout group fights it for the rect, and the map would stretch.
            var imageGo = EditorUIHelpers.CreateUI("PreviewImage", frame.transform);
            EditorUIHelpers.StretchFill(imageGo);
            _previewImage = imageGo.AddComponent<RawImage>();
            _previewImage.raycastTarget = true;
            _previewFitter = imageGo.AddComponent<AspectRatioFitter>();
            _previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            var pointer = imageGo.AddComponent<SeedWorldPreviewPointer>();
            pointer.Bind(this, _previewImage.rectTransform);

            _hoverLabel = EditorUIHelpers.AddLabel(_previewContent, "Pasa el raton por el mapa.", 11f);
            _hoverLabel.color = EditorUIHelpers.TEXT_SECONDARY;

            _statsLabel = EditorUIHelpers.AddLabel(_previewContent, "", 11f);
            _statsLabel.color = EditorUIHelpers.TEXT_SECONDARY;
            _statsLabel.enableWordWrapping = true;

            var legend = EditorUIHelpers.CreateUI("Legend", _previewContent);
            var legendLe = legend.AddComponent<LayoutElement>();
            legendLe.preferredHeight = 80f;
            legendLe.flexibleHeight = 0f;
            var grid = legend.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132f, 18f);
            grid.spacing = new Vector2(4f, 2f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            _legendRoot = legend.transform;
        }

        private void AddLayerButton(Transform parent, string label, PreviewLayer layer)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, () => SetLayer(layer), 24f, 11f);
            btn.gameObject.name = "Layer_" + layer;
            _layerButtons.Add(btn);
        }

        // ── Refresh ────────────────────────────────────────────────────────────

        private void RefreshPreview()
        {
            if (_map == null) return;

            for (int i = 0; i < _layerButtons.Count; i++)
                if (_layerButtons[i] != null)
                    UIButton.SetTint(_layerButtons[i],
                        (int)_layer == i ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL);

            PaintTexture();
            RefreshStats();
            RefreshLegend();
        }

        private void PaintTexture()
        {
            int cols = _map.Columns, rows = _map.Rows;

            if (_previewTexture == null || _previewTexture.width != cols || _previewTexture.height != rows)
            {
                ReleasePreviewTexture();
                _previewTexture = new Texture2D(cols, rows, TextureFormat.RGBA32, false)
                {
                    name = "SeedWorldPreview",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                _pixels = new Color32[cols * rows];
            }

            var s = _map.Climate.Settings;
            float rareThreshold = 1f - s.rarity * WorldClimate.RareBand;

            // Map row 0 is the SOUTH edge and texture row 0 is the bottom, so the index is shared.
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = ColourFor(i, s, rareThreshold);

            if (_map.HasSpawn) MarkSpawn(cols, rows);

            _previewTexture.SetPixels32(_pixels);
            _previewTexture.Apply(false);

            if (_previewImage != null) _previewImage.texture = _previewTexture;
            if (_previewFitter != null) _previewFitter.aspectRatio = (float)cols / rows;
        }

        private Color32 ColourFor(int i, WorldGenSettings s, float rareThreshold)
        {
            switch (_layer)
            {
                case PreviewLayer.Elevation:
                    return WorldGenPalette.ElevationColor(_map.Elevation[i], s.seaLevel, s.mountainLevel);
                case PreviewLayer.Temperature:
                    return WorldGenPalette.TemperatureColor(_map.Temperature[i]);
                case PreviewLayer.Humidity:
                    return WorldGenPalette.HumidityColor(_map.Humidity[i]);
                case PreviewLayer.Rarity:
                    return WorldGenPalette.RarityColor(_map.Rarity[i], rareThreshold);
                default:
                    // Towns ride the biome layer: they are places on the ground, and a separate
                    // layer would hide where they sit relative to rivers and coasts.
                    var biome = WorldBiomeTable.GetAt(_map.Biomes[i]).PreviewColor;
                    if (_map.TownMask[i] == 2) return WorldGenPalette.Street;
                    if (_map.TownMask[i] == 1) return Color32.Lerp(biome, WorldGenPalette.TownArea, 0.35f);
                    return biome;
            }
        }

        /// <summary>A small plus on the spawn cell, so "where does a run begin" is on the map.</summary>
        private void MarkSpawn(int cols, int rows)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(_map.SpawnTile.x / _map.TilesPerCell), 0, cols - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(_map.SpawnTile.y / _map.TilesPerCell), 0, rows - 1);
            for (int d = -SPAWN_MARK_RADIUS; d <= SPAWN_MARK_RADIUS; d++)
            {
                SetPixel(cols, rows, cx + d, cy);
                SetPixel(cols, rows, cx, cy + d);
            }
        }

        private void SetPixel(int cols, int rows, int x, int y)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows) return;
            _pixels[y * cols + x] = WorldGenPalette.Spawn;
        }

        private void RefreshStats()
        {
            if (_statsLabel == null) return;
            var s = _map.Climate.Settings;
            float land = _map.LandFraction;
            float water = _map.Fraction(WorldBiome.Ocean) + _map.Fraction(WorldBiome.DeepOcean);

            string spawn = _map.HasSpawn
                ? $"inicio ({Mathf.RoundToInt(_map.SpawnTile.x)}, {Mathf.RoundToInt(_map.SpawnTile.y)})"
                : "sin tierra para empezar";

            _statsLabel.text =
                $"Semilla {s.seed}   {s.widthTiles}x{s.heightTiles} tiles   " +
                $"tierra {Pct(land)}  agua {Pct(water)}   {spawn}\n" +
                $"Vista previa {_map.Columns}x{_map.Rows} celdas " +
                $"({_map.TilesPerCell.ToString("0.##", CultureInfo.InvariantCulture)} tiles/celda) " +
                $"generada en {_lastGenerateMs} ms";
        }

        /// <summary>Only the biomes that actually appear, largest first.</summary>
        private void RefreshLegend()
        {
            if (_legendRoot == null) return;
            ClearChildren(_legendRoot);

            var present = new List<WorldBiome>();
            for (int i = 0; i < WorldBiomeTable.Count; i++)
                if (_map.BiomeCounts[i] > 0) present.Add((WorldBiome)i);
            present.Sort((a, b) => _map.BiomeCounts[(int)b].CompareTo(_map.BiomeCounts[(int)a]));

            foreach (var biome in present)
            {
                var info = WorldBiomeTable.Get(biome);
                var cell = EditorUIHelpers.CreateUI("Legend_" + biome, _legendRoot);
                var hlg = cell.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;

                var swatch = EditorUIHelpers.CreateUI("Swatch", cell.transform);
                var img = swatch.AddComponent<Image>();
                img.color = info.PreviewColor;
                img.raycastTarget = false;
                var swatchLe = swatch.AddComponent<LayoutElement>();
                swatchLe.preferredWidth = 12f;
                swatchLe.flexibleWidth = 0f;

                var label = EditorUIHelpers.AddLabel(cell.transform, $"{info.DisplayName} {Pct(_map.Fraction(biome))}", 10f);
                label.color = EditorUIHelpers.TEXT_SECONDARY;
                label.raycastTarget = false;
                var labelLe = label.gameObject.AddComponent<LayoutElement>();
                labelLe.flexibleWidth = 1f;
            }
        }

        /// <summary>
        /// Called by <see cref="SeedWorldPreviewPointer"/> with the pointer in 0..1 over the image,
        /// or a negative value when it left. Reads the SAME map the texture was painted from.
        /// </summary>
        internal void OnPreviewHover(Vector2 uv)
        {
            if (_hoverLabel == null) return;
            if (_map == null || uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f)
            {
                _hoverLabel.text = "Pasa el raton por el mapa.";
                return;
            }

            int col = Mathf.Clamp(Mathf.FloorToInt(uv.x * _map.Columns), 0, _map.Columns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * _map.Rows), 0, _map.Rows - 1);
            _hoverLabel.text = DescribeCell(col, row);
        }

        internal string DescribeCell(int col, int row)
        {
            int i = _map.Index(col, row);
            var tile = _map.CellCentre(col, row);
            var info = WorldBiomeTable.GetAt(_map.Biomes[i]);
            return $"({Mathf.RoundToInt(tile.x)}, {Mathf.RoundToInt(tile.y)})  {info.DisplayName}   " +
                   $"altura {F2(_map.Elevation[i])}  temp {F2(_map.Temperature[i])}  " +
                   $"humedad {F2(_map.Humidity[i])}  rareza {F2(_map.Rarity[i])}";
        }

        private static string Pct(float f) => Mathf.RoundToInt(f * 100f) + "%";

        private static string F2(float f) => f.ToString("0.00", CultureInfo.InvariantCulture);

        private void ReleasePreviewTexture()
        {
            if (_previewTexture == null) return;
            if (Application.isPlaying) Destroy(_previewTexture);
            else DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }
    }
}
