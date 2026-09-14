using System;
using System.Diagnostics;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Build a generated world into a slot and walk the player into it: the ONE sequence the Seed
    /// World editor's "Construir" button and the <c>seedworld nueva</c> console command both run,
    /// so a world started from the console is the same world the editor would have built.
    ///
    /// <para><b>Rebuilding the slot that is loaded right now leaves it first.</b>
    /// <c>LoadMapSlot</c> begins by mirroring the live zone list INTO the outgoing slot's file;
    /// building the active slot and then loading it would copy the old zones straight over the
    /// file the build just wrote.</para>
    /// </summary>
    public static class SeedWorldLauncher
    {
        public sealed class Outcome
        {
            public SeedWorldBakeResult Result;
            public bool Loaded;
            public long LoadMs;
            public string Error;

            public bool Succeeded => Error == null && Result != null && Result.Succeeded;
        }

        public static Outcome BuildAndLoad(WorldGenSettings settings, string slot, bool live)
        {
            var outcome = new Outcome();
            if (!Application.isPlaying) { outcome.Error = "Solo en Play Mode."; return outcome; }

            var mgr = MapEditorManager.Instance;
            if (mgr == null) { outcome.Error = "No hay MapEditorManager en esta escena."; return outcome; }

            var request = SeedWorldBakeRequest.ForSlot(slot);
            if (request == null) { outcome.Error = "Nombre de mapa no valido (vacio o 'default')."; return outcome; }
            if (SeedWorldBaker.Inspect(request) == SeedWorldBaker.SlotState.Foreign)
            {
                outcome.Error = $"'{request.Slot}' es un mapa hecho a mano. Elige otro nombre.";
                return outcome;
            }

            // The catalogues arrive with the boot sequence. Built before them, a world has streets and
            // no house, no tree and no altar — and it is still a valid slot, so nothing would say so.
            var loader = UnityEngine.Object.FindObjectOfType<BuildingLoader>();
            var catalog = loader != null ? loader.Catalog : null;
            Valkur.Core.ServiceLocator.TryGet<SpawnerTemplateCatalog>(out var spawners);
            if (catalog == null || spawners == null)
            {
                outcome.Error = "El juego aun esta cargando (sin catalogo de edificios o de spawners). Prueba en unos segundos.";
                return outcome;
            }

            if (string.Equals(mgr.ActiveMapSlot, request.Slot, StringComparison.OrdinalIgnoreCase))
                mgr.LoadMapSlot(MapEditorMapSlots.DEFAULT_SLOT);

            var palette = new SeedWorldTilePalette(TerrainCatalogLoader.Load());

            outcome.Result = live
                ? SeedWorldBaker.BakeLive(settings, request, palette, catalog, spawners)
                : SeedWorldBaker.Bake(settings, request, palette, catalog, spawners);
            if (!outcome.Result.Succeeded) { outcome.Error = outcome.Result.Error; return outcome; }

            var watch = Stopwatch.StartNew();
            outcome.Loaded = mgr.LoadMapSlot(request.Slot);

            // A live world has no ground until the streamer paints it. Doing the zones around the
            // spawn now, inside the load, means the first frame the player sees is a world and not
            // a void they cannot walk out of.
            var streamer = SeedWorldLiveStreamer.Instance;
            if (live && outcome.Loaded && streamer != null)
            {
                streamer.RequestResync();
                streamer.OpenNow();
                streamer.SyncAll(outcome.Result.SpawnWorld);
            }
            watch.Stop();
            outcome.LoadMs = watch.ElapsedMilliseconds;
            return outcome;
        }

        public static string Describe(Outcome o)
        {
            if (o == null) return string.Empty;
            if (!o.Succeeded) return "No se construyo: " + o.Error;
            var r = o.Result;
            string ground = r.Live
                ? "suelo en vivo"
                : $"{r.BlockedTiles} tiles bloqueados, {r.HardCuts} cortes sin transicion";
            return $"'{r.Slot}' ({(r.Live ? "en vivo" : "horneado")}): {r.ZonesX}x{r.ZonesY} zonas, {r.Rivers} rios, " +
                   $"{r.Towns} pueblos con {r.Buildings} edificios, {r.Trees} arboles, {r.Spawners} spawners, {ground}. " +
                   $"Generado {r.GenerateMs} ms, escrito {r.WriteMs} ms ({r.Bytes / 1024f:0} KB), " +
                   $"cargado {o.LoadMs} ms." + (o.Loaded ? string.Empty : " NO SE PUDO CARGAR.");
        }
    }
}
