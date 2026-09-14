namespace Valkur.Data.WorldGen
{
    /// <summary>The four climate channels at one point of the world, each 0..1.</summary>
    public readonly struct WorldClimateSample
    {
        public readonly float Elevation;
        public readonly float Temperature;
        public readonly float Humidity;
        public readonly float Rarity;

        public WorldClimateSample(float elevation, float temperature, float humidity, float rarity)
        {
            Elevation = elevation;
            Temperature = temperature;
            Humidity = humidity;
            Rarity = rarity;
        }
    }
}
