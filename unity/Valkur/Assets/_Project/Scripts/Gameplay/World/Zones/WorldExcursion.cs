using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A trip OUT of Pepitoria to another map slot — a generated world, an authored test map — and
    /// the return ticket that brings the player back to the exact spot the trip started from.
    ///
    /// <para><b>Pepitoria is the hub.</b> Every other place is reached from it and returns to it
    /// (project decision, 2026-09-14). A trip is therefore always measured from home: a second map
    /// visited while already away keeps the ORIGINAL home and only moves the destination.</para>
    ///
    /// <para><b>Why a ticket and not just "load default".</b> Everything that persisted the player
    /// while away recorded a position that means nothing in Pepitoria: an autosave taken in a
    /// generated world stored the player in "Pueblo inicial", a zone the base world does not have.
    /// While a ticket exists the save records the ticket's home instead
    /// (<c>SaveService.ResolvePersistablePlayerPosition</c>), so what the player gains away is kept
    /// and where they are is never corrupted; arriving home spends the ticket and lands the player
    /// on it; and a session that ENDS away starts the next one at home.</para>
    ///
    /// <para><b>On disk</b> as <c>persistentDataPath/Maps/_excursion.json</c>, so a crash or a
    /// closed window cannot lose it — the underscore is the Maps directory's reserved prefix, so the
    /// slot explorer never lists it. During a test run it lives in memory only.</para>
    /// </summary>
    public static class WorldExcursion
    {
        public const string FileName = "_excursion.json";

        [Serializable]
        private sealed class TicketFile
        {
            public float homeX;
            public float homeY;
            public string homeZone = "";
            public string destination = "";
            public string leftAtUtc = "";
        }

        private static TicketFile s_ticket;
        private static bool s_loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            s_ticket = null;
            s_loaded = false;
        }

        /// <summary>True while the player is on a trip away from Pepitoria.</summary>
        public static bool IsAway
        {
            get { EnsureLoaded(); return s_ticket != null; }
        }

        /// <summary>The map the trip is on right now; null at home.</summary>
        public static string Destination
        {
            get { EnsureLoaded(); return s_ticket?.destination; }
        }

        /// <summary>Where the trip started, i.e. where a save taken away from home puts the player.</summary>
        public static bool TryGetHome(out Vector2 position, out string zone)
        {
            EnsureLoaded();
            if (s_ticket == null) { position = Vector2.zero; zone = ""; return false; }
            position = new Vector2(s_ticket.homeX, s_ticket.homeY);
            zone = s_ticket.homeZone ?? "";
            return true;
        }

        /// <summary>
        /// Leave Pepitoria for <paramref name="destination"/>. Called by the map load at the moment
        /// the active slot changes. While already away only the destination moves: home stays the
        /// spot the FIRST step out was taken from.
        /// </summary>
        public static void Leave(Vector2 homePosition, string homeZone, string destination)
        {
            EnsureLoaded();
            if (s_ticket != null)
            {
                s_ticket.destination = destination ?? "";
            }
            else
            {
                s_ticket = new TicketFile
                {
                    homeX = homePosition.x,
                    homeY = homePosition.y,
                    homeZone = homeZone ?? "",
                    destination = destination ?? "",
                    leftAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                Debug.Log($"[WorldExcursion] Left Pepitoria at ({homePosition.x:0.##}, {homePosition.y:0.##}) " +
                          $"in '{homeZone}' for '{destination}'.");
            }
            Persist();
        }

        /// <summary>
        /// Arrive home: spends the ticket and answers where to put the player down. False when there
        /// was no trip to come back from.
        /// </summary>
        public static bool TryArriveHome(out Vector2 position, out string zone)
        {
            if (!TryGetHome(out position, out zone)) return false;
            Debug.Log($"[WorldExcursion] Back in Pepitoria at ({position.x:0.##}, {position.y:0.##}) " +
                      $"from '{s_ticket.destination}'.");
            Discard();
            return true;
        }

        /// <summary>Forget the trip without travelling back.</summary>
        public static void Discard()
        {
            EnsureLoaded();
            s_ticket = null;
            Persist();
        }

        /// <summary>
        /// A ticket still on disk when a session starts means the last one ended away from home. This
        /// one starts at home: the active-slot pointer is put back before anything loads a world, and
        /// the ticket is spent — the save it left behind already holds the home position.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartAtHomeAfterASessionThatEndedAway()
        {
            EnsureLoaded();
            if (s_ticket == null) return;
            MapEditorMapSlots.WriteActiveSlotOnDisk(MapEditorMapSlots.DEFAULT_SLOT);
            Debug.Log($"[WorldExcursion] The last session ended away in '{s_ticket.destination}'. " +
                      $"This one starts in Pepitoria at ({s_ticket.homeX:0.##}, {s_ticket.homeY:0.##}).");
            Discard();
        }

        /// <summary>For test fixtures: start from "at home" with nothing read from disk.</summary>
        public static void ResetForTests()
        {
            s_ticket = null;
            s_loaded = true;
        }

        // ── Storage ─────────────────────────────────────────────────────────────

        // The whole test run, allow scope or not: that scope is opened by fixtures that PARK the
        // Maps files they exercise, and none of them parks this one — a real ticket left by a
        // session that ended away would be read, moved or deleted by a fixture's trip.
        private static bool DiskIsOffLimits => WorldDataWriteGuard.TestRunActive;

        private static string TicketPath => Path.Combine(Application.persistentDataPath, "Maps", FileName);

        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            if (DiskIsOffLimits) return;
            try
            {
                if (!File.Exists(TicketPath)) return;
                var parsed = JsonUtility.FromJson<TicketFile>(File.ReadAllText(TicketPath));
                if (parsed != null) s_ticket = parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldExcursion] Unreadable ticket at '{TicketPath}' ignored: {ex.Message}");
            }
        }

        private static void Persist()
        {
            if (DiskIsOffLimits) return;
            try
            {
                if (s_ticket == null)
                {
                    if (File.Exists(TicketPath)) File.Delete(TicketPath);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(TicketPath));
                string tmp = TicketPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s_ticket, prettyPrint: true));
                if (File.Exists(TicketPath)) File.Delete(TicketPath);
                File.Move(tmp, TicketPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldExcursion] Could not write the return ticket: {ex.Message}");
            }
        }
    }
}
