using System.Text;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The <c>minimap</c> console probe.
    ///
    /// <para>The map is assembled from four independent sources — a progressive bake, a fog
    /// grid, a per-frame item collection and a view — and each can fail in a way the others
    /// hide: a map with no terrain looks exactly like a map whose fog is total. This answers
    /// "which half is wrong" in one line, the way <c>boot</c> and <c>spawners</c> do for
    /// their subsystems.</para>
    /// </summary>
    public sealed partial class MinimapHUD
    {
        private bool _consoleRegistered;
        private float _frameMs;

        /// <summary>Smoothed milliseconds this component spends per frame, bake included.</summary>
        public float FrameMilliseconds => _frameMs;

        private void RegisterConsoleCommand()
        {
            if (_consoleRegistered || !DevConsole.HasInstance) return;
            _consoleRegistered = true;
            DevConsole.Instance.RegisterCommand(new DevConsole.ConsoleCommand
            {
                Name     = "minimap",
                Aliases  = new[] { "mapa" },
                Usage    = "minimap [rebake|reveal <radio>|fog clear|pin <x> <y>|pin clear|zoom <radio>]",
                Help     = "estado del minimapa: horneado, niebla, iconos y coste por frame",
                Category = "hud",
                Handler  = args => DevConsole.Instance.Print(RunCommand(args)),
            });
        }

        /// <summary>Execute a <c>minimap</c> command and return its report. Public for tests.</summary>
        public string RunCommand(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            switch (sub)
            {
                case "rebake":
                    _baker?.MarkAllDirty();
                    return "[minimap] Terreno marcado para rehornear; se rellena de cerca a lejos.";

                case "reveal":
                {
                    float r = args.Length > 2 && float.TryParse(args[2], out var v) ? v : 600f;
                    var p = Valkur.Core.EntityRegistry.PlayerTransform;
                    Vector2 c = p != null ? (Vector2)p.position : Vector2.zero;
                    _manager.Fog.Reveal(c, r);
                    return $"[minimap] Revelado un radio de {r:0} alrededor de {c}.";
                }

                case "fog":
                    if (args.Length > 2 && args[2].ToLowerInvariant() == "clear")
                    {
                        _manager.Fog.ClearActive();
                        return "[minimap] Niebla del mundo actual olvidada.";
                    }
                    return "[minimap] Uso: minimap fog clear";

                case "pin":
                    if (args.Length > 2 && args[2].ToLowerInvariant() == "clear")
                    {
                        MinimapWaypoint.Clear();
                        return "[minimap] Destino quitado.";
                    }
                    if (args.Length > 3 && float.TryParse(args[2], out var x) && float.TryParse(args[3], out var y))
                    {
                        MinimapWaypoint.Set(new Vector2(x, y));
                        return $"[minimap] Destino en ({x:0}, {y:0}).";
                    }
                    return "[minimap] Uso: minimap pin <x> <y> | minimap pin clear";

                case "zoom":
                    if (args.Length > 2 && float.TryParse(args[2], out var z))
                    {
                        _manager.SetViewRadius(z);
                        return $"[minimap] Radio visible {_manager.ViewRadius:0.0}.";
                    }
                    return "[minimap] Uso: minimap zoom <radio 8..64>";

                default:
                    return Report();
            }
        }

        /// <summary>One-screen status of every half of the map.</summary>
        public string Report()
        {
            var sb = new StringBuilder(512);
            if (_baker != null)
            {
                var r = _baker.AtlasWorldRect;
                sb.Append("[minimap] terreno ").Append(_baker.ChunkCount - _baker.PendingChunks).Append('/').Append(_baker.ChunkCount)
                  .Append(" trozos, ").Append(_baker.PixelsPerUnit).Append(" px/u, mundo (")
                  .Append(r.x).Append(", ").Append(r.y).Append(") ").Append(r.z).Append('x').Append(r.w)
                  .Append(", horneados ").Append(_baker.BakedTotal).AppendLine();
            }
            var fog = _manager.Fog;
            var fr = fog.WorldRect;
            sb.Append("  niebla '").Append(fog.ActiveKey).Append("' capa ").Append(fr.z).Append('x').Append(fr.w)
              .Append(_manager.FogOfWarEnabled ? "" : " (desactivada)").AppendLine();

            int enemies = 0, landmarks = 0, quests = 0;
            foreach (var it in _scene.Items)
            {
                if (it.Layer == MinimapLayer.Entity) enemies++;
                else if (it.Layer == MinimapLayer.Landmark) landmarks++;
                else if (it.Layer == MinimapLayer.Quest) quests++;
            }
            sb.Append("  iconos: ").Append(enemies).Append(" criaturas, ").Append(landmarks).Append(" lugares, ")
              .Append(quests).Append(" misiones/destino; particulas ").Append(_fx.Count).AppendLine();
            sb.Append("  zoom ").Append(_displayRadius.ToString("0.0")).Append(" -> ").Append(_manager.ViewRadius.ToString("0.0"))
              .Append(", coste ").Append(_frameMs.ToString("0.00")).Append(" ms/frame")
              .Append(WorldMapOpen ? ", mapa del mundo ABIERTO" : string.Empty);
            return sb.ToString();
        }
    }
}
