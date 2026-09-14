using System.Globalization;
using UnityEngine;
using Valkur.Core.Rendering;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>look</c> command: the switches and dials of the world's look layers.
    ///
    /// Every layer here is something the day/night cycle, the weather or an entity drives on
    /// its own — nothing needs the console to work — so this exists for the same reason the
    /// <c>weather</c> family does: it is the programmatic seam. A PlayMode probe or an agent
    /// on <c>execute_code</c> can switch the bloom off, take a capture, switch it on and take
    /// another, and the difference IS the measurement of the layer. Without it, "does the bloom
    /// help" is a matter of opinion.
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "look" category.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterLookCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "look",
                Usage    = "look [bloom|clouds|sunshadows|sway|fireflies|footsteps] [on|off|<value>]",
                Help     = "show or set the world's look layers; 'look bloom 0.5' sets the intensity",
                Category = "look",
                Handler  = args => CmdLook(args)
            });
        }

        private void CmdLook(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                ReportLook();
                return;
            }

            string layer = args[1].ToLowerInvariant();
            string value = args.Length >= 3 ? args[2].ToLowerInvariant() : "";

            switch (layer)
            {
                case "bloom":
                    if (value == "")
                    {
                        Log($"[look] bloom {(ScreenGradeSettings.BloomEnabled ? "on" : "off")} " +
                            $"intensity {ScreenGradeSettings.BloomIntensity:F2} threshold {ScreenGradeSettings.BloomThreshold:F2}");
                        return;
                    }
                    if (value == "off") { ScreenGradeSettings.BloomIntensity = 0f; Log("[look] bloom off"); return; }
                    if (value == "on")  { ScreenGradeSettings.BloomIntensity = ScreenGradeSettings.DefaultBloomIntensity; Log("[look] bloom on"); return; }
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float intensity))
                    {
                        ScreenGradeSettings.BloomIntensity = Mathf.Clamp(intensity, 0f, 3f);
                        Log($"[look] bloom intensity {ScreenGradeSettings.BloomIntensity:F2}");
                        return;
                    }
                    Log("[look] usage: look bloom on|off|<0..3>");
                    return;

                case "clouds":     Toggle(value, v => WorldLookSettings.CloudShadows = v, WorldLookSettings.CloudShadows, "clouds");     return;
                case "sunshadows": Toggle(value, v => WorldLookSettings.SunShadows   = v, WorldLookSettings.SunShadows,   "sunshadows"); return;
                case "sway":       Toggle(value, v => WorldLookSettings.WindSway     = v, WorldLookSettings.WindSway,     "sway");       return;
                case "fireflies":  Toggle(value, v => WorldLookSettings.Fireflies    = v, WorldLookSettings.Fireflies,    "fireflies");  return;
                case "footsteps":  Toggle(value, v => WorldLookSettings.Footsteps    = v, WorldLookSettings.Footsteps,    "footsteps");  return;

                default:
                    Log($"[look] unknown layer '{args[1]}'. Layers: bloom, clouds, sunshadows, sway, fireflies, footsteps.");
                    return;
            }
        }

        private void Toggle(string value, System.Action<bool> set, bool current, string name)
        {
            if (value == "")      { Log($"[look] {name} {(current ? "on" : "off")}"); return; }
            if (value == "on")    { set(true);  Log($"[look] {name} on");  return; }
            if (value == "off")   { set(false); Log($"[look] {name} off"); return; }
            if (value == "toggle"){ set(!current); Log($"[look] {name} {(!current ? "on" : "off")}"); return; }
            Log($"[look] usage: look {name} on|off");
        }

        private void ReportLook()
        {
            Log($"[look] bloom      {(ScreenGradeSettings.BloomEnabled && ScreenGradeSettings.BloomIntensity > 0.001f ? "on " : "off")}  intensity {ScreenGradeSettings.BloomIntensity:F2}");
            Log($"[look] clouds     {(WorldLookSettings.CloudShadows ? "on" : "off")}");
            Log($"[look] sunshadows {(WorldLookSettings.SunShadows ? "on" : "off")}");
            Log($"[look] sway       {(WorldLookSettings.WindSway ? "on" : "off")}");
            Log($"[look] fireflies  {(WorldLookSettings.Fireflies ? "on" : "off")}");
            Log($"[look] footsteps  {(WorldLookSettings.Footsteps ? "on" : "off")}");
        }
    }
}
