using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>door</c> command family: author and test building doorways without leaving the
    /// game and without opening F10.
    ///
    /// Every command routes through the SAME internal seams the F10 Door panel uses
    /// (<c>BuildingsRuntimeEditor.TrySetDoor</c> and friends), so a door authored here is
    /// undoable, persisted by the same writer, and identical on disk to one authored by
    /// clicking. That is deliberate: a console path that wrote <c>overrides.door</c> itself
    /// would be a second serializer to keep in step with the reader, which is exactly the
    /// shape of the spawner coordinate-space drift incident.
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "doors" category and
    /// reachable programmatically through <see cref="DevConsole.Execute"/>, so a PlayMode test
    /// or an agent can drive the whole author-then-walk-through loop with no Game view.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterDoorCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "door",
                Usage = "door [id <n>] [<overlay> [x y] | clear | on | off | anchor <x> <y> | size <s> | enter]",
                Help = "inspect or author a doorway - on the building nearest the player, or on 'id <n>'",
                Category = "doors",
                Handler = CmdDoor,
                Completer = _ => ListOverlayFileNames()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "doors",
                Usage = "doors",
                Help = "list every placed building that has a doorway, and where it leads",
                Category = "doors",
                Handler = _ => CmdDoors()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "overlays",
                Usage = "overlays",
                Help = "list the overlay files in StreamingAssets/Maps that a doorway can target",
                Category = "doors",
                Handler = _ => CmdOverlays()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "leave",
                Usage = "leave",
                Help = "walk back out of the interior the player is currently in",
                Category = "doors",
                Handler = _ => CmdLeave()
            });
        }

        // ── door ────────────────────────────────────────────────────────────────────

        private void CmdDoor(string[] args)
        {
            var editor = BuildingsRuntimeEditor.Instance;
            if (editor == null)
            {
                Log("[door] BuildingsRuntimeEditor is not in the scene — nothing can author a doorway.");
                return;
            }

            // "door id <n> ..." addresses one building explicitly. Worth having: the player
            // is a Dynamic body and a solid building footprint pushes them back out, so
            // "stand on it and type door" is not reproducible for a scripted run - which is
            // exactly how this feature has to be verified end to end.
            BuildingObject building;
            float distance;
            if (args != null && args.Length >= 3 &&
                string.Equals(args[1], "id", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wantedId))
            {
                building = FindBuildingById(wantedId);
                if (building == null)
                {
                    Log($"[door] No placed building with ID {wantedId}. Run 'doors' to list the ones that have doorways.");
                    return;
                }
                distance = DistanceToPlayer(building);

                // Drop the "id <n>" pair so the sub-command parsing below is identical for
                // both addressing modes.
                var rest = new string[args.Length - 2];
                rest[0] = args[0];
                for (int i = 3; i < args.Length; i++) rest[i - 2] = args[i];
                args = rest;
            }
            else
            {
                building = FindBuildingNearestPlayer(out distance);
                if (building == null)
                {
                    Log("[door] No placed building found. Stand near one and try again.");
                    return;
                }
            }

            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "";

            switch (sub)
            {
                case "":
                    ReportDoorState(building, distance);
                    return;

                case "clear":
                    editor.TryClearDoor(building, out string clearMsg);
                    Log("[door] " + clearMsg);
                    return;

                case "on":
                case "off":
                    editor.TrySetTemplateHasDoor(building.Template, sub == "on", out string toggleMsg);
                    Log("[door] " + toggleMsg);
                    return;

                case "anchor":
                    if (args.Length < 4 ||
                        !TryParseCoord(args[2], out float ax) ||
                        !TryParseCoord(args[3], out float ay))
                    {
                        Log("[door] Usage: door anchor <x> <y>   (fractions of the sprite, 0..1)");
                        return;
                    }
                    editor.TrySetTemplateAnchor(building.Template, new Vector2(ax, ay), null, out string anchorMsg);
                    Log("[door] " + anchorMsg);
                    return;

                case "size":
                    if (args.Length < 3 || !TryParseCoord(args[2], out float size))
                    {
                        Log("[door] Usage: door size <s>   (fraction of the sprite, 0..1)");
                        return;
                    }
                    editor.TrySetTemplateAnchor(building.Template, null, size, out string sizeMsg);
                    Log("[door] " + sizeMsg);
                    return;

                case "enter":
                    CmdDoorEnter(building);
                    return;

                default:
                    CmdDoorSetTarget(editor, building, args);
                    return;
            }
        }

        private void CmdDoorSetTarget(BuildingsRuntimeEditor editor, BuildingObject building, string[] args)
        {
            string target = args[1];

            float spawnX = 0f, spawnY = 0f;
            if (args.Length >= 4)
            {
                if (!TryParseCoord(args[2], out spawnX) || !TryParseCoord(args[3], out spawnY))
                {
                    Log("[door] Usage: door <overlay> [x y]   — x and y are world units, e.g. 25.5");
                    return;
                }
            }
            else
            {
                // No spawn given: land in the middle of the destination rather than on its
                // (0,0) corner, which reads as being flung out of the room.
                spawnX = WorldTransitionService.DEFAULT_SPAWN.x;
                spawnY = WorldTransitionService.DEFAULT_SPAWN.y;
            }

            if (editor.TrySetDoor(building, target, spawnX, spawnY, out string message))
                Log($"[door] {message} Spawn ({spawnX:0.##}, {spawnY:0.##}).");
            else
                Log("[door] " + message);
        }

        private void CmdDoorEnter(BuildingObject building)
        {
            var door = BuildingDoorFactory.Find(building);
            if (door == null)
            {
                Log($"[door] ID {building.InstanceId} has no live doorway to walk through.");
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            Log(door.Enter(player != null ? player.gameObject : null)
                ? $"[door] Entered through ID {building.InstanceId}."
                : $"[door] ID {building.InstanceId} refused the transition — see the Unity console.");
        }

        private void ReportDoorState(BuildingObject building, float distance)
        {
            var t = building.Template;
            Log($"[door] Nearest building: ID {building.InstanceId} ({(t != null ? t.name : "no template")}), " +
                $"{distance:0.0} u away.");
            Log($"       Template doorway: {(building.TemplateDeclaresDoor ? "YES" : "no")}" +
                (t != null
                    ? $"  anchor ({t.doorOffsetNormalized.x:0.00}, {t.doorOffsetNormalized.y:0.00}) " +
                      $"size {t.doorSizeNormalized.x:0.00}"
                    : ""));

            var spec = building.DoorSpec;
            Log(spec != null && spec.IsValid
                ? $"       Leads to: {spec.target} at ({spec.spawnX:0.##}, {spec.spawnY:0.##})"
                : "       Leads to: nowhere");
            Log("       Set one with:  door <overlay> [x y]     List targets with:  overlays");
        }

        // ── doors ───────────────────────────────────────────────────────────────────

        private void CmdDoors()
        {
            var all = FindObjectsOfType<BuildingObject>()
                .Where(b => b != null && b.DoorSpec != null && b.DoorSpec.IsValid)
                .OrderBy(b => b.InstanceId)
                .ToList();

            if (all.Count == 0)
            {
                Log("[door] No placed building has a doorway yet. Stand next to one and run: door <overlay>");
                return;
            }

            Log($"[door] {all.Count} doorway(s):");
            foreach (var b in all)
            {
                bool live = BuildingDoorFactory.Find(b) != null;
                Log($"   ID {b.InstanceId,-4} {b.Template?.name,-28} -> {b.DoorSpec.target}" +
                    (live ? "" : "   (NOT LIVE — template hasDoor is off)"));
            }
        }

        // ── overlays ────────────────────────────────────────────────────────────────

        private void CmdOverlays()
        {
            var names = ListOverlayFileNames();
            if (names.Length == 0)
            {
                Log("[door] No overlay files found in StreamingAssets/Maps.");
                return;
            }

            Log($"[door] {names.Length} overlay(s) in StreamingAssets/Maps:");
            foreach (var n in names) Log("   " + n);
        }

        /// <summary>
        /// Overlay filenames a doorway can target. Read from disk on demand — there is no
        /// cached index of them anywhere, and this runs only on a Tab press or an explicit
        /// listing, never per frame.
        /// </summary>
        private static string[] ListOverlayFileNames()
        {
            try
            {
                string dir = Path.Combine(Application.streamingAssetsPath, "Maps");
                if (!Directory.Exists(dir)) return Array.Empty<string>();
                // Recursive on purpose: interiors live in Maps/Interiors/ rather than beside
                // the zone overlays, because everything directly under Maps/ is a 50x50 tile
                // of the base world and is asserted to be exactly that size. Names are
                // returned RELATIVE to Maps/ with forward slashes - the exact string a
                // doorway stores and OverlayLoader resolves.
                return Directory.GetFiles(dir, "*.overlay.json", SearchOption.AllDirectories)
                                .Select(full => full.Substring(dir.Length)
                                                    .TrimStart(Path.DirectorySeparatorChar,
                                                               Path.AltDirectorySeparatorChar)
                                                    .Replace('\\', '/'))
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToArray();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DevConsole] Could not list overlays: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        // ── leave ───────────────────────────────────────────────────────────────────

        private void CmdLeave()
        {
            if (string.IsNullOrEmpty(WorldTransitionService.CurrentOverlay) &&
                !WorldTransitionService.HasReturnPoint)
            {
                Log("[door] The player is already in the base world.");
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            Log(WorldTransitionService.ReturnToCaller(player != null ? player.gameObject : null)
                ? "[door] Returned to the base world."
                : "[door] Could not return — see the Unity console.");
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        private static bool TryParseCoord(string raw, out float value)
            => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>Placed building with this instance id, or null.</summary>
        private static BuildingObject FindBuildingById(int instanceId)
        {
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].InstanceId == instanceId) return all[i];
            return null;
        }

        private static float DistanceToPlayer(BuildingObject b)
        {
            var playerTransform = EntityRegistry.PlayerTransform;
            Vector2 origin = playerTransform != null ? (Vector2)playerTransform.position : Vector2.zero;
            return b.TryGetWorldRect(out var rect)
                ? Vector2.Distance(origin, rect.center)
                : Vector2.Distance(origin, b.transform.position);
        }

        /// <summary>
        /// The placed building the player is most plausibly talking about: one whose sprite
        /// they are standing on wins outright, and only then does distance decide.
        ///
        /// Distance alone gets a big building wrong. A house is anchored at its bottom-centre
        /// and can be thirteen units across, so a player standing at its front door is nearer
        /// to the centre of a tree across the street than to the centre of the house they are
        /// touching. Containment first is what makes "walk up to it and type door" mean what
        /// the author expects.
        /// </summary>
        private static BuildingObject FindBuildingNearestPlayer(out float distance)
        {
            distance = float.MaxValue;

            var playerTransform = EntityRegistry.PlayerTransform;
            Vector2 origin = playerTransform != null ? (Vector2)playerTransform.position : Vector2.zero;

            BuildingObject best = null;
            bool bestContains = false;

            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;

                bool hasRect  = b.TryGetWorldRect(out var rect);
                bool contains = hasRect && rect.Contains(origin);
                float d = hasRect
                    ? Vector2.Distance(origin, rect.center)
                    : Vector2.Distance(origin, b.transform.position);

                // A building under the player always beats one that is merely close; between
                // two of the same kind, the nearer centre wins.
                if (bestContains && !contains) continue;
                if (!bestContains && contains) { bestContains = true; distance = d; best = b; continue; }
                if (d >= distance) continue;

                distance = d;
                best = b;
            }
            return best;
        }
    }
}
