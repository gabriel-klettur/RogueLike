using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// THE answer to "what is at this point of the world". Samples the four climate channels
    /// and classifies them into a biome.
    ///
    /// <para><b>Single owner.</b> The Seed World preview, the phase-2 bake and the phase-5 chunk
    /// streamer all call <see cref="BiomeAt"/> / <see cref="Sample"/> + <see cref="Classify"/>.
    /// A preview that re-derived what the generator "should" produce would draw what the author
    /// configured while the build did something else — the defect the spell-area overlay was
    /// built to catch.</para>
    ///
    /// <para><b>Coordinates are tiles, origin at the bottom-left corner of the map</b>, Y
    /// growing north. Pure and deterministic: no Unity objects, no <c>UnityEngine.Random</c>, no
    /// clock. Construct once per settings value; sampling allocates nothing.</para>
    /// </summary>
    public sealed class WorldClimate
    {
        // ── Decision constants (rules of the vocabulary, not per-profile tuning) ──

        /// <summary>Below sea level by more than this, water is deep.</summary>
        public const float DeepBand = 0.12f;

        /// <summary>Above sea level by less than this, land is shore.</summary>
        public const float ShoreBand = 0.02f;

        /// <summary>Highland at least this hot is volcanic rather than rock.</summary>
        public const float VolcanicHeat = 0.72f;

        /// <summary>How much colder the highest peak is than the coast.</summary>
        public const float AltitudeChill = 0.35f;

        /// <summary>At rarity 1, the top this-fraction of the rarity channel is rare land.</summary>
        public const float RareBand = 0.3f;

        /// <summary>The rarity channel is wetter-than-this for enchanted, drier for corrupted.</summary>
        public const float RareHumiditySplit = 0.5f;

        /// <summary>Fraction of the half-map over which edge falloff ramps from sea to full land.</summary>
        public const float EdgeBand = 0.4f;

        private const int ElevationSalt = 1;
        private const int TemperatureSalt = 2;
        private const int HumiditySalt = 3;
        private const int RaritySalt = 4;

        private readonly WorldGenSettings _settings;
        private readonly FractalNoise2D _elevation;
        private readonly FractalNoise2D _temperature;
        private readonly FractalNoise2D _humidity;
        private readonly FractalNoise2D _rarity;

        private readonly bool[] _enabled;
        private readonly WorldBiome[] _landBiomes;
        private readonly float[] _landTemperature;
        private readonly float[] _landHumidity;
        private readonly float[] _landWeight;

        /// <summary>A clamped private COPY of the settings this climate was built from.</summary>
        public WorldGenSettings Settings => _settings;

        public WorldClimate(WorldGenSettings settings)
        {
            _settings = (settings ?? new WorldGenSettings()).Clone() ?? new WorldGenSettings();
            _settings.Clamp();

            int seed = _settings.seed;
            _elevation   = new FractalNoise2D(WorldSeed.Derive(seed, ElevationSalt), _settings.detailOctaves);
            _temperature = new FractalNoise2D(WorldSeed.Derive(seed, TemperatureSalt), 3);
            _humidity    = new FractalNoise2D(WorldSeed.Derive(seed, HumiditySalt), 3);
            _rarity      = new FractalNoise2D(WorldSeed.Derive(seed, RaritySalt), 3);

            _enabled = new bool[WorldBiomeTable.Count];
            int landCount = 0;
            for (int i = 0; i < _enabled.Length; i++)
            {
                _enabled[i] = _settings.WeightOf((WorldBiome)i).enabled;
                if (_enabled[i] && WorldBiomeTable.GetAt(i).Kind == WorldBiomeKind.Land) landCount++;
            }

            _landBiomes = new WorldBiome[landCount];
            _landTemperature = new float[landCount];
            _landHumidity = new float[landCount];
            _landWeight = new float[landCount];

            int k = 0;
            for (int i = 0; i < _enabled.Length; i++)
            {
                var info = WorldBiomeTable.GetAt(i);
                if (!_enabled[i] || info.Kind != WorldBiomeKind.Land) continue;
                _landBiomes[k] = info.Biome;
                _landTemperature[k] = info.IdealTemperature;
                _landHumidity[k] = info.IdealHumidity;
                _landWeight[k] = _settings.WeightOf(info.Biome).weight;
                k++;
            }
        }

        public bool IsEnabled(WorldBiome biome) => _enabled[(int)biome];

        public WorldBiome BiomeAt(float x, float y) => Classify(Sample(x, y));

        public WorldClimateSample Sample(float x, float y)
        {
            var s = _settings;

            float elevation = _elevation.Sample(x / s.continentScale, y / s.continentScale);
            elevation *= Mathf.Lerp(1f, EdgeMask(x, y), s.edgeFalloff);

            float climateX = x / s.climateScale;
            float climateY = y / s.climateScale;

            float north = s.heightTiles > 0 ? y / s.heightTiles - 0.5f : 0f;
            float aboveSea = s.seaLevel < 1f ? Mathf.Max(0f, elevation - s.seaLevel) / (1f - s.seaLevel) : 0f;

            float temperature = _temperature.Sample(climateX, climateY)
                                + s.temperatureBias
                                - s.latitude * north
                                - AltitudeChill * aboveSea;

            float humidity = _humidity.Sample(climateX, climateY) + s.humidityBias;

            // The same scale as the climate: rare land comes in regions the size of a climate
            // zone, and only the top of the channel qualifies, so it never reads as freckles.
            float rarity = _rarity.Sample(climateX, climateY);

            return new WorldClimateSample(
                Mathf.Clamp01(elevation), Mathf.Clamp01(temperature), Mathf.Clamp01(humidity), rarity);
        }

        /// <summary>
        /// 1 in the interior, falling to 0 at the map's edge over <see cref="EdgeBand"/> of the
        /// half-size. Smoothstepped, or the coast would follow the map's rectangle in a straight
        /// line wherever the falloff dominates the noise.
        /// </summary>
        private float EdgeMask(float x, float y)
        {
            var s = _settings;
            float half = 0.5f * Mathf.Min(s.widthTiles, s.heightTiles);
            if (half <= 0f) return 1f;

            float d = Mathf.Min(Mathf.Min(x, s.widthTiles - x), Mathf.Min(y, s.heightTiles - y)) / half;
            float t = Mathf.Clamp01(d / EdgeBand);
            return t * t * (3f - 2f * t);
        }

        public WorldBiome Classify(in WorldClimateSample c)
        {
            var s = _settings;
            float e = c.Elevation;

            // WATER. A disabled water biome does not become a hole: the cell falls through to
            // the land rules, which is what "no oceans" means to the author.
            if (e < s.seaLevel)
            {
                bool deep = e < s.seaLevel - DeepBand;
                if (deep && IsEnabled(WorldBiome.DeepOcean)) return WorldBiome.DeepOcean;
                if (IsEnabled(WorldBiome.Ocean)) return WorldBiome.Ocean;
                if (IsEnabled(WorldBiome.DeepOcean)) return WorldBiome.DeepOcean;
            }
            else if (e < s.seaLevel + ShoreBand && IsEnabled(WorldBiome.Beach))
            {
                return WorldBiome.Beach;
            }

            // HIGHLAND.
            if (e >= s.mountainLevel)
            {
                if (c.Temperature >= VolcanicHeat && IsEnabled(WorldBiome.Volcanic)) return WorldBiome.Volcanic;
                if (IsEnabled(WorldBiome.Mountain)) return WorldBiome.Mountain;
            }

            // RARE.
            if (s.rarity > 0f && c.Rarity > 1f - s.rarity * RareBand)
            {
                bool wet = c.Humidity >= RareHumiditySplit;
                var first = wet ? WorldBiome.Enchanted : WorldBiome.Corrupted;
                var second = wet ? WorldBiome.Corrupted : WorldBiome.Enchanted;
                if (IsEnabled(first)) return first;
                if (IsEnabled(second)) return second;
            }

            // LAND: the nearest enabled climate point, weighted.
            return NearestLand(c.Temperature, c.Humidity);
        }

        private WorldBiome NearestLand(float temperature, float humidity)
        {
            if (_landBiomes.Length == 0) return WorldBiome.Plains;

            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _landBiomes.Length; i++)
            {
                float dt = temperature - _landTemperature[i];
                float dh = humidity - _landHumidity[i];
                float score = (dt * dt + dh * dh) / _landWeight[i];
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return _landBiomes[best];
        }
    }
}
