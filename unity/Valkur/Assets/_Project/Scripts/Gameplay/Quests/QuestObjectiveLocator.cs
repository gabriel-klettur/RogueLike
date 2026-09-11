using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Answers "where does this objective want the player to be", or says it has no answer.
    ///
    /// <para><b>Half the objective kinds have no place, and that is the point.</b> A
    /// Collect is satisfied wherever the item happens to be, a Craft at any station, a
    /// Survive right here, an EarnCoins and a ReachLevel nowhere at all. Inventing a
    /// destination for those would be worse than none: an arrow that points somewhere
    /// arbitrary teaches the player to distrust every arrow. <see cref="TryLocate"/>
    /// returns false and the marker layer draws nothing.</para>
    ///
    /// <para><b>A monster target is the NEAREST LIVE one, not a spawn table.</b> "Kill 10
    /// barbol" has no fixed address — the honest answer is the one the player can act on,
    /// and if none is alive the honest answer is silence rather than the last place one
    /// stood.</para>
    /// </summary>
    public static class QuestObjectiveLocator
    {
        /// <summary>
        /// World position for <paramref name="objective"/>, when it has one.
        /// </summary>
        public static bool TryLocate(IObjective objective, out Vector2 position, out string label)
        {
            position = default;
            label = string.Empty;
            if (objective == null || objective.IsComplete) return false;

            switch (objective)
            {
                case TalkObjective talk:
                    return TryLocatePersona(talk.PersonaId, out position, out label);

                case ReachZoneObjective reach:
                    return TryLocateZone(reach.ZoneName, out position, out label);

                case KillCountObjective kill:
                    return TryLocateNearestMonster(kill.MonsterKey, out position, out label);

                default:
                    // Collect, Craft, CastSpell, Survive, ReachLevel, EarnCoins.
                    return false;
            }
        }

        /// <summary>
        /// Where a character with <paramref name="personaId"/> is standing right now.
        ///
        /// <para>Resolved by scanning the live <c>NPCChatIdentity</c> components rather
        /// than from any authored coordinate: five of the seven characters WALK, so a
        /// placed position would point at where a vendor used to be — the same defect
        /// <c>InteractableRegistry.RegisterDynamic</c> exists to prevent.</para>
        /// </summary>
        public static bool TryLocatePersona(string personaId, out Vector2 position, out string label)
        {
            position = default;
            label = string.Empty;
            if (string.IsNullOrEmpty(personaId)) return false;

            var identities = UnityEngine.Object.FindObjectsOfType<NPCChatIdentity>();
            for (int i = 0; i < identities.Length; i++)
            {
                var persona = identities[i].Persona;
                if (persona == null) continue;
                if (!string.Equals(persona.personaId, personaId, StringComparison.OrdinalIgnoreCase)) continue;

                position = identities[i].transform.position;
                label = ShortLabel(persona.displayName);
                return true;
            }
            return false;
        }

        private static bool TryLocateZone(string zoneName, out Vector2 position, out string label)
        {
            position = default;
            label = string.Empty;
            if (string.IsNullOrEmpty(zoneName)) return false;

            var zm = ServiceLocator.Get<ZoneManager>() ?? UnityEngine.Object.FindObjectOfType<ZoneManager>();
            if (zm == null) return false;

            var centre = zm.GetZoneCenter(zoneName);
            // GetZoneCenter answers zero for a name it does not know, and (0,0) is a real
            // world position — so an unknown zone would silently plant a marker at the
            // world origin and send the player to the corner of the map.
            if (centre == Vector2.zero) return false;

            position = centre;
            label = ShortLabel(zoneName);
            return true;
        }

        private static bool TryLocateNearestMonster(string monsterKey, out Vector2 position, out string label)
        {
            position = default;
            label = string.Empty;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) return false;

            var brains = UnityEngine.Object.FindObjectsOfType<FSMMonsterBrain>();
            float best = float.MaxValue;
            bool found = false;

            for (int i = 0; i < brains.Length; i++)
            {
                var def = brains[i].Definition;
                if (def == null) continue;

                // An empty key means "anything", which is what the vigil quest authors.
                if (!string.IsNullOrEmpty(monsterKey) &&
                    !string.Equals(def.monsterKey, monsterKey, StringComparison.OrdinalIgnoreCase)) continue;

                var health = brains[i].GetComponent<Health>();
                if (health != null && health.IsDead) continue;

                float d = ((Vector2)brains[i].transform.position - (Vector2)player.position).sqrMagnitude;
                if (d >= best) continue;

                best = d;
                position = brains[i].transform.position;
                label = ShortLabel(string.IsNullOrEmpty(def.displayName) ? def.monsterKey : def.displayName);
                found = true;
            }
            return found;
        }

        /// <summary>
        /// A caption a minimap can hold. The disc is 192 px across and a marker label sits
        /// beside a 4 px dot, so anything past a few characters overlaps its neighbour —
        /// the same reason the Controls editor asks the device for SHORT key legends.
        /// </summary>
        private static string ShortLabel(string full)
        {
            if (string.IsNullOrEmpty(full)) return string.Empty;
            return full.Length <= 8 ? full : full.Substring(0, 8);
        }
    }
}
