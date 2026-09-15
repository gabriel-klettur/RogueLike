using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Gameplay.World.Generation
{
    internal static class MiniJsonRuntimeAccess
    {
        public static Dictionary<string, Vector2> Zones(string slotJson)
        {
            var root = (Dictionary<string, object>)Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(slotJson);
            var map = new Dictionary<string, Vector2>();
            foreach (var z in (List<object>)root["zones"])
            {
                var d = (Dictionary<string, object>)z;
                map[(string)d["zoneName"]] = new Vector2(System.Convert.ToInt32(d["gridOffsetX"]), System.Convert.ToInt32(d["gridOffsetY"]));
            }
            return map;
        }
    }
}
