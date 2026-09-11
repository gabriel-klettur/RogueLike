using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Puts the quest layer's points of interest on <see cref="WorldMarkerBoard"/> so the
    /// minimap can draw them without either assembly referencing the other.
    ///
    /// <para>Three things go up, and the order they are computed in is the order of
    /// urgency: a finished quest waiting to be handed in, then the objectives of what is
    /// already accepted, then who has new work. A player with something to deliver should
    /// not have to pick their turn-in out of a field of exclamation marks.</para>
    ///
    /// <para><b>Republished on a timer, not per frame.</b> Every entry costs a
    /// <c>FindObjectsOfType</c> — the persona scan and the monster scan — and none of these
    /// facts changes faster than a person walks. <see cref="RepublishInterval"/> is half a
    /// second; the markers are drawn from the last snapshot in between, which for a wandering
    /// vendor is at most half a second of lag on a dot the size of four pixels.</para>
    /// </summary>
    public sealed class QuestMarkerPublisher : MonoBehaviour
    {
        /// <summary>Channel name on the board. One publisher, one channel.</summary>
        public const string Channel = "quests";

        /// <summary>How often the world is re-scanned for marker positions.</summary>
        public const float RepublishInterval = 0.5f;

        private readonly List<WorldMarker> _markers = new List<WorldMarker>();
        private readonly List<QuestDefinition> _scratch = new List<QuestDefinition>();

        /// <summary>What each persona should be advertising over its head this pass.</summary>
        private readonly Dictionary<string, QuestBadgeState> _badgeStates =
            new Dictionary<string, QuestBadgeState>(StringComparer.OrdinalIgnoreCase);

        private float _timer;

        private void OnDisable()
        {
            // A publisher that stops must take its markers with it, or the last snapshot is
            // drawn forever over a world it no longer describes.
            WorldMarkerBoard.Clear(Channel);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < RepublishInterval) return;
            _timer = 0f;
            Republish();
        }

        /// <summary>Recompute and publish. Public so a test can drive it without a clock.</summary>
        public void Republish()
        {
            _markers.Clear();

            var service = QuestService.Instance;
            if (service == null || service.Manager == null)
            {
                WorldMarkerBoard.Publish(Channel, _markers);
                return;
            }

            _badgeStates.Clear();

            AddTurnIns(service);
            AddActiveObjectives(service);
            AddOffers(service);
            AddInProgressBadges(service);

            WorldMarkerBoard.Publish(Channel, _markers);
            ApplyBadges();
        }

        /// <summary>
        /// Records the mark a persona should show, keeping the most urgent one.
        ///
        /// <para>A character can be all three at once — Gatita offers the banquet, is
        /// waiting on the pantry, and gave you a third that is half done. Showing the
        /// highest state is the only version that stays useful: three marks stacked over
        /// one head is noise, and picking the LAST one computed would make the glyph depend
        /// on the order the scans happen to run in.</para>
        /// </summary>
        private void RaiseBadge(string personaId, QuestBadgeState state)
        {
            if (string.IsNullOrEmpty(personaId)) return;
            if (_badgeStates.TryGetValue(personaId, out var existing) && existing >= state) return;
            _badgeStates[personaId] = state;
        }

        /// <summary>
        /// The quiet mark: a character whose quest the player is still working on.
        ///
        /// <para>It carries no marker on the minimap on purpose — the OBJECTIVES already
        /// do, and a dot on the giver as well would point the player back at the person
        /// who sent them instead of at the work.</para>
        /// </summary>
        private void AddInProgressBadges(QuestService service)
        {
            foreach (string id in service.Manager.ActiveIds)
            {
                var def = service.Manager.GetActiveDefinition(id);
                if (def == null) continue;
                RaiseBadge(def.giverPersonaId, QuestBadgeState.InProgress);
                RaiseBadge(def.turnInPersonaId, QuestBadgeState.InProgress);
            }
        }

        /// <summary>
        /// Pushes this pass's answer onto every character in the world, including the ones
        /// that must go BACK to showing nothing.
        ///
        /// <para>That second half is what makes it correct rather than merely working: a
        /// pass that only touched the personas it found something for would leave the last
        /// exclamation mark hanging over a character the player has already dealt with,
        /// and the badge would be a record of the first thing they ever had.</para>
        /// </summary>
        private void ApplyBadges()
        {
            var identities = UnityEngine.Object.FindObjectsOfType<NPCChatIdentity>();
            for (int i = 0; i < identities.Length; i++)
            {
                var persona = identities[i].Persona;
                if (persona == null || string.IsNullOrEmpty(persona.personaId)) continue;

                if (!_badgeStates.TryGetValue(persona.personaId, out var state))
                    state = QuestBadgeState.None;

                // Only create the component for a character that has something to show.
                // Adding one to all seven personas up front would put an inert
                // MonoBehaviour and a hidden SpriteRenderer on every villager for the
                // whole session.
                var badge = identities[i].GetComponent<QuestGiverBadge>();
                if (badge == null)
                {
                    if (state == QuestBadgeState.None) continue;
                    badge = identities[i].gameObject.AddComponent<QuestGiverBadge>();
                }
                badge.SetState(state);
            }
        }

        /// <summary>Quests that are done and want reporting: the most urgent thing on screen.</summary>
        private void AddTurnIns(QuestService service)
        {
            foreach (string id in service.Manager.ActiveIds)
            {
                var def = service.Manager.GetActiveDefinition(id);
                if (def == null || string.IsNullOrEmpty(def.turnInPersonaId)) continue;

                // TurnInsReadyFor is keyed by persona, so ask it about this quest's own
                // turn-in rather than re-deriving "is the authored work done" here — two
                // implementations of that question is how the marker and the button that
                // closes the quest end up disagreeing.
                service.TurnInsReadyFor(def.turnInPersonaId, _scratch);
                if (!_scratch.Contains(def)) continue;

                RaiseBadge(def.turnInPersonaId, QuestBadgeState.TurnIn);

                if (QuestObjectiveLocator.TryLocatePersona(def.turnInPersonaId, out var pos, out string label))
                    _markers.Add(new WorldMarker(pos, WorldMarkerKind.QuestTurnIn, label));
            }
        }

        /// <summary>
        /// Where the open work is. Only objectives that HAVE a place produce a marker —
        /// see <see cref="QuestObjectiveLocator"/> for why half of them do not.
        /// </summary>
        private void AddActiveObjectives(QuestService service)
        {
            foreach (string id in service.Manager.ActiveIds)
            {
                var quest = service.Manager.GetActiveQuest(id);
                if (quest == null) continue;

                var def = service.Manager.GetActiveDefinition(id);

                // Walk only the AUTHORED objectives. The generated turn-in is the last
                // entry when a quest declares one, and AddTurnIns already owns it — measured
                // live, including it put two markers on the same character: a big green
                // plus with a small blue diamond drawn over the middle of it, which muddies
                // the one dot the player most needs to read. Identified by position, the
                // same rule QuestService.AuthoredWorkDone uses, so the two cannot drift.
                int count = quest.Objectives.Count;
                if (def != null && !string.IsNullOrEmpty(def.turnInPersonaId)) count--;

                for (int i = 0; i < count; i++)
                {
                    var obj = quest.Objectives[i];
                    if (obj == null || obj.IsComplete) continue;

                    if (QuestObjectiveLocator.TryLocate(obj, out var pos, out string label))
                        _markers.Add(new WorldMarker(pos, WorldMarkerKind.QuestObjective, label));
                }
            }
        }

        /// <summary>
        /// Characters with something to give. This is the half of "discovery" a minimap can
        /// carry — before it, finding work meant walking up to all seven personas in turn.
        /// </summary>
        private void AddOffers(QuestService service)
        {
            var catalog = service.Catalog;
            if (catalog == null) return;

            // Walk the DISTINCT givers rather than every quest: a character with three
            // offers is still one dot, and three stacked markers on one pixel is a brighter
            // dot rather than more information.
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < catalog.quests.Count; i++)
            {
                var q = catalog.quests[i];
                if (q == null || string.IsNullOrEmpty(q.giverPersonaId)) continue;
                if (!seen.Add(q.giverPersonaId)) continue;

                service.OffersFor(q.giverPersonaId, _scratch);
                if (_scratch.Count == 0) continue;

                RaiseBadge(q.giverPersonaId, QuestBadgeState.Offer);

                if (QuestObjectiveLocator.TryLocatePersona(q.giverPersonaId, out var pos, out string label))
                    _markers.Add(new WorldMarker(pos, WorldMarkerKind.QuestOffer, label));
            }
        }
    }
}
