namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Atmospheric weather effects that can be combined freely (e.g. Wind + Rain
    /// = stormy). The <see cref="WeatherManager"/> tracks each independently
    /// rather than a single exclusive enum so designers can compose effects.
    /// </summary>
    public enum WeatherType
    {
        Wind,
        Rain,
        Snow,
    }
}
