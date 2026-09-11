using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>quest</c> family: what am I carrying, what could I take, and give me
    /// that one now.
    ///
    /// <para><b>Why this exists.</b> A quest is written by a catalogue, offered by a
    /// persona, advanced by six different event sources and two polled ones, closed by
    /// a conversation, paid out by four reward systems and persisted through the save
    /// document. Nine places, each of which can fail in a way the other eight hide —
    /// and the failure mode of almost all of them is SILENCE: a quest that never ticks
    /// looks exactly like a quest the player has not worked on yet. Without a probe,
    /// "did that kill count" is only answerable by killing another one and squinting
    /// at a HUD row.</para>
    ///
    /// <para>It is also, until the offer panel lands in the chat gutter, the only way
    /// to ACCEPT a quest whose giver you cannot reach — which is the honest state of
    /// the feature rather than something to hide behind a UI that is not there yet.</para>
    ///
    /// <para>Bare <c>quest</c> REPORTS rather than doing anything, the same rule
    /// <c>altar</c> follows: a command that mutates player state on a bare invocation
    /// is one somebody runs by accident while looking for its syntax.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private readonly List<QuestDefinition> _questScratch = new List<QuestDefinition>();

        private void RegisterQuestCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "quest",
                Usage    = "quest [<id>|start <id>|abandon <id>|offers [persona]]",
                Help     = "report the quest log; start, abandon or list what is on offer",
                Category = "quests",
                Handler  = args => CmdQuest(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "questlog",
                Usage    = "questlog [on|off|min|max]",
                Help     = "show, hide or collapse the quest tracker window",
                Category = "quests",
                Handler  = args => CmdQuestLog(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "quests",
                Usage    = "quests [all]",
                Help     = "list active quests with per-objective progress ('all' adds the catalogue)",
                Category = "quests",
                Handler  = args => CmdQuests(args)
            });
        }

        // ── questlog ───────────────────────────────────────────────────────────

        /// <summary>
        /// The tracker window's three verbs from the console.
        ///
        /// <para><b>This is the reopen path a PLAYER has</b>, and it is why the window is
        /// allowed a close button at all. The Quests editor can bring it back too, and editors
        /// are gated out of a player build — so without this the only way back from one click
        /// on the X would be accepting another quest.</para>
        ///
        /// <para>Bare <c>questlog</c> REPORTS rather than toggling, the same rule <c>quest</c>
        /// and <c>altar</c> follow.</para>
        /// </summary>
        private void CmdQuestLog(string[] args)
        {
            var hud = HUD.QuestLogHUD.Instance;
            if (hud == null) { Log("No hay seguidor de misiones en esta escena."); return; }

            string verb = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : null;
            switch (verb)
            {
                case "on":  hud.SetClosed(false); break;
                case "off": hud.SetClosed(true); break;
                case "min": hud.SetMinimized(true); break;
                case "max": hud.SetMinimized(false); break;
                case null:  break;
                default:
                    Log("Uso: questlog [on|off|min|max]");
                    return;
            }

            Log($"Seguidor: {(hud.IsWindowVisible ? "visible" : "oculto")}, " +
                $"{(hud.IsMinimized ? "minimizado" : "desplegado")}.");
        }

        // ── quest ──────────────────────────────────────────────────────────────

        private void CmdQuest(string[] args)
        {
            var service = QuestService.Instance;
            if (service == null || service.Manager == null)
            {
                Log("No QuestService in the scene. Nothing can be offered or tracked.");
                return;
            }

            if (args == null || args.Length == 0) { ReportActive(service); return; }

            string verb = args[0].ToLowerInvariant();
            switch (verb)
            {
                case "start":
                case "accept":
                    if (args.Length < 2) { Log("Usage: quest start <id>"); return; }
                    StartQuestById(service, args[1]);
                    return;

                case "abandon":
                case "drop":
                    if (args.Length < 2) { Log("Usage: quest abandon <id>"); return; }
                    if (!service.Manager.IsActive(args[1])) { Log($"'{args[1]}' is not active."); return; }
                    service.Manager.AbandonQuest(args[1]);
                    Log($"Abandoned '{args[1]}'.");
                    return;

                case "offers":
                    ReportOffers(service, args.Length > 1 ? args[1] : null);
                    return;

                default:
                    ReportOne(service, args[0]);
                    return;
            }
        }

        /// <summary>
        /// Starts a quest by id, saying WHY when it refuses. "Nothing happened" is the
        /// single worst answer a quest console can give, because it is also what a
        /// working command looks like when the quest is already in the log.
        /// </summary>
        private void StartQuestById(QuestService service, string id)
        {
            var catalog = service.Catalog;
            if (catalog == null) { Log("No QuestCatalog loaded — run Valkur > Quests > Seed Quest Content."); return; }

            var def = catalog.Find(id);
            if (def == null) { Log($"No quest with id '{id}'. Try 'quests all'."); return; }

            if (service.Manager.IsActive(id))    { Log($"'{id}' is already in the log."); return; }
            if (service.Manager.IsCompleted(id)) { Log($"'{id}' is already completed."); return; }

            int level = QuestService.PlayerLevel();
            if (def.requiredLevel > level)
                Log($"NOTE: requires level {def.requiredLevel}, player is {level} — forcing it on anyway.");

            foreach (string pre in def.prerequisiteQuestIds)
                if (!string.IsNullOrEmpty(pre) && !service.Manager.IsCompleted(pre))
                    Log($"NOTE: prerequisite '{pre}' is not completed — forcing it on anyway.");

            // Deliberately StartQuest and not TryAccept: the console's job is to reach a
            // state the game cannot yet reach on its own, and a probe that refuses on the
            // same grounds as the UI can only ever confirm what the UI already showed.
            Log(service.Manager.StartQuest(def)
                ? $"Started '{def.questId}' — {def.displayName}."
                : $"Refused to start '{id}'.");
        }

        private void ReportActive(QuestService service)
        {
            var manager = service.Manager;
            var sb = new StringBuilder();
            sb.AppendLine($"Quests: {manager.ActiveIds.Count} active, {manager.CompletedIds.Count} completed. " +
                          $"Catalogue: {(service.Catalog != null ? service.Catalog.quests.Count : 0)}.");

            foreach (string id in manager.ActiveIds)
            {
                var quest = manager.GetActiveQuest(id);
                var def   = manager.GetActiveDefinition(id);
                if (quest == null) continue;
                sb.AppendLine($"  {id} — {def?.displayName} " +
                              $"({Mathf.RoundToInt(quest.OverallProgress * 100f)}%)");
            }
            if (manager.ActiveIds.Count == 0)
                sb.AppendLine("  (nothing active — try 'quest offers' or 'quest start <id>')");

            Log(sb.ToString().TrimEnd());
        }

        private void ReportOne(QuestService service, string id)
        {
            var manager = service.Manager;
            var quest   = manager.GetActiveQuest(id);
            if (quest == null)
            {
                var def = service.Catalog != null ? service.Catalog.Find(id) : null;
                if (def == null) { Log($"No quest with id '{id}'."); return; }
                Log($"'{id}' — {def.displayName}. " +
                    $"{(manager.IsCompleted(id) ? "COMPLETED" : "not active")}. " +
                    $"Level {def.requiredLevel}, giver '{def.giverPersonaId}', " +
                    $"{def.objectives.Length} objectives, {def.xpReward} xp / {def.coinReward} coins.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{id} — {quest.DisplayName}");
            foreach (var obj in quest.Objectives)
            {
                if (obj == null) continue;
                sb.AppendLine($"  [{(obj.IsComplete ? "x" : " ")}] {obj.Description}  " +
                              $"{obj.Current}/{obj.Target}");
            }
            Log(sb.ToString().TrimEnd());
        }

        private void ReportOffers(QuestService service, string personaId)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(personaId))
            {
                service.OffersFor(personaId, _questScratch);
                sb.AppendLine($"'{personaId}' offers {_questScratch.Count}:");
                foreach (var q in _questScratch)
                    sb.AppendLine($"  {q.questId} — {q.displayName} (nivel {q.requiredLevel})");
                Log(sb.ToString().TrimEnd());
                return;
            }

            // No persona named: walk every giver in the catalogue, so the answer is
            // "here is everything the world could hand you right now" rather than
            // "here is what the character you happen to be standing next to has".
            var catalog = service.Catalog;
            if (catalog == null) { Log("No QuestCatalog loaded."); return; }

            var givers = new List<string>();
            foreach (var q in catalog.quests)
            {
                if (q == null || string.IsNullOrEmpty(q.giverPersonaId)) continue;
                if (!givers.Contains(q.giverPersonaId)) givers.Add(q.giverPersonaId);
            }

            int total = 0;
            foreach (string giver in givers)
            {
                service.OffersFor(giver, _questScratch);
                if (_questScratch.Count == 0) continue;
                sb.AppendLine($"{giver}:");
                foreach (var q in _questScratch)
                {
                    sb.AppendLine($"  {q.questId} — {q.displayName} (nivel {q.requiredLevel})");
                    total++;
                }
            }
            sb.Insert(0, $"{total} quests available right now (player level {QuestService.PlayerLevel()}):\n");
            Log(sb.ToString().TrimEnd());
        }

        // ── quests ─────────────────────────────────────────────────────────────

        private void CmdQuests(string[] args)
        {
            var service = QuestService.Instance;
            if (service == null || service.Manager == null) { Log("No QuestService in the scene."); return; }

            bool all = args != null && args.Length > 0 &&
                       args[0].Equals("all", System.StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            var manager = service.Manager;

            sb.AppendLine($"ACTIVE ({manager.ActiveIds.Count}):");
            foreach (string id in manager.ActiveIds)
            {
                var quest = manager.GetActiveQuest(id);
                if (quest == null) continue;
                sb.AppendLine($"  {id} — {quest.DisplayName}");
                foreach (var obj in quest.Objectives)
                    if (obj != null)
                        sb.AppendLine($"      [{(obj.IsComplete ? "x" : " ")}] {obj.Description} " +
                                      $"{obj.Current}/{obj.Target}");
            }

            sb.AppendLine($"COMPLETED ({manager.CompletedIds.Count}):");
            foreach (string id in manager.CompletedIds) sb.AppendLine($"  {id}");

            if (all && service.Catalog != null)
            {
                sb.AppendLine($"CATALOGUE ({service.Catalog.quests.Count}):");
                foreach (var q in service.Catalog.quests)
                {
                    if (q == null) continue;
                    string state = manager.IsCompleted(q.questId) ? "done"
                                 : manager.IsActive(q.questId)    ? "active"
                                 : "available";
                    sb.AppendLine($"  {q.questId,-24} n{q.requiredLevel,-3} {state,-10} {q.displayName}");
                }
            }

            Log(sb.ToString().TrimEnd());
        }
    }
}
