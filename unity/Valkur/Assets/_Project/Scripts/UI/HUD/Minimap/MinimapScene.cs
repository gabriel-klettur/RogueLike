using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>Draw order bands. Later bands draw over earlier ones.</summary>
    public enum MinimapLayer : byte { Ground = 0, Entity = 1, Landmark = 2, Quest = 3 }

    /// <summary>One thing to draw on a map, in world space.</summary>
    public struct MinimapItem
    {
        public Vector2 World;
        public MinimapIcon Icon;
        public Color Color;
        public float Size;
        public MinimapLayer Layer;
        /// <summary>Clamp to the rim with a chevron when out of view, instead of dropping it.</summary>
        public bool EdgePin;
        /// <summary>Breathing glow under the glyph, 0 for none.</summary>
        public float Halo;
        /// <summary>Hover a little, the way a quest mark does.</summary>
        public bool Bob;
        /// <summary>Lift above whatever stands at the same spot (a quest mark over its giver).</summary>
        public bool Badge;
        /// <summary>Stable identity, so a view can tell a NEW marker from one it already showed.</summary>
        public int Key;
        /// <summary>How the fog of war hides this item. See <see cref="MinimapReveal"/>.</summary>
        public MinimapReveal Reveal;
        /// <summary>What the hover tooltip says. Never null.</summary>
        public string Label;
    }

    /// <summary>
    /// What the fog of war lets the player know about an item.
    ///
    /// <para>The first build drew every vendor and every monster in the world over the
    /// unexplored ink — the alchemist's flask sat in a region the player had never been to,
    /// which is a spoiler and makes exploring pointless. A creature can only be SEEN (it moves;
    /// knowing where it was is not knowing where it is), a place can be REMEMBERED once found,
    /// and a quest mark is always known — the errand is what told the player where to go.</para>
    /// </summary>
    public enum MinimapReveal : byte
    {
        /// <summary>Always drawn: quest marks, the pin, the player's corpse, their own allies.</summary>
        Always = 0,
        /// <summary>Drawn once the ground under it is explored: vendors, doors, portals, altars.</summary>
        Explored = 1,
        /// <summary>Drawn only inside the player's current sight: enemies and villagers.</summary>
        Seen = 2,
    }

    /// <summary>
    /// Everything the maps draw besides the terrain, collected once per frame from every
    /// source that knows about a place: entity dots, landmark markers, the quest board,
    /// the resurrection altars, the player's own corpse and the player's waypoint.
    ///
    /// <para><b>One collector, two views.</b> The minimap and the world map show the same
    /// facts at different scales; collecting them twice would be two chances for the two maps
    /// to disagree about what is where — which is exactly the shape the altar trail and the
    /// minimap already shipped with (the death system drew its own path and neither knew the
    /// other existed).</para>
    ///
    /// <para><b>Order is meaning.</b> Items are sorted by <see cref="MinimapLayer"/>, so a
    /// quest mark is always drawn over the vendor who offers it. The old minimap drew the
    /// board BEFORE the entity dots, and a gold quest diamond sat underneath a gold vendor
    /// square of the same size: every quest in town was invisible on the map.</para>
    /// </summary>
    public sealed class MinimapScene
    {
        private readonly List<MinimapItem> _items = new List<MinimapItem>(128);
        private readonly List<MinimapItem>[] _bands =
        {
            new List<MinimapItem>(16), new List<MinimapItem>(64), new List<MinimapItem>(32), new List<MinimapItem>(16),
        };
        private readonly Dictionary<int, CachedEntity> _entityCache = new Dictionary<int, CachedEntity>();
        private DeathSequenceController _death;
        private float _nextDeathLookup;

        private struct CachedEntity
        {
            public MinimapEntityKind Kind;
            public MinimapIcon VendorIcon;
            public string Name;
            public float Until;
        }

        /// <summary>Items collected by the last <see cref="Collect"/>, sorted by layer.</summary>
        public IReadOnlyList<MinimapItem> Items => _items;

        /// <summary>True while the player is a spirit looking for an altar.</summary>
        public bool PlayerIsSpirit { get; private set; }

        /// <summary>Gather every item. Call once per frame, before any view draws.</summary>
        public void Collect(MinimapStyle s, float time)
        {
            _items.Clear();
            for (int i = 0; i < _bands.Length; i++) _bands[i].Clear();
            ResolveDeath(time);
            PlayerIsSpirit = _death != null && _death.CurrentPhase == DeathSequenceController.Phase.Spirit;

            CollectEntities(s, time);
            CollectMarkers(s);
            CollectBoard(s);
            CollectDeath(s);
            CollectWaypoint(s);

            // Concatenate the bands in order: stable within a band (no flicker between two
            // glyphs that overlap), and no comparison delegate allocated every frame.
            for (int i = 0; i < _bands.Length; i++) _items.AddRange(_bands[i]);
        }

        private void Add(MinimapItem item) => _bands[(int)item.Layer].Add(item);

        private void ResolveDeath(float time)
        {
            if (_death != null || time < _nextDeathLookup) return;
            _nextDeathLookup = time + 2f;
            _death = Object.FindObjectOfType<DeathSequenceController>();
        }

        // ── Sources ────────────────────────────────────────────────────────

        private void CollectEntities(MinimapStyle s, float time)
        {
            var dots = MinimapManager.Dots;
            for (int i = 0; i < dots.Count; i++)
            {
                var dot = dots[i];
                if (dot == null || !dot.isActiveAndEnabled) continue;
                var go = dot.gameObject;
                var c = Resolve(go, dot.DotType, time);
                var item = new MinimapItem { World = dot.transform.position, Layer = MinimapLayer.Entity, Key = go.GetInstanceID(), Reveal = MinimapReveal.Seen, Label = c.Name };
                switch (c.Kind)
                {
                    case MinimapEntityKind.Enemy:
                        item.Icon = MinimapIcon.Dot; item.Color = s.enemyColor; item.Size = s.enemySize; break;
                    case MinimapEntityKind.Elite:
                        item.Icon = MinimapIcon.EliteDot; item.Color = s.eliteColor; item.Size = s.eliteSize; break;
                    case MinimapEntityKind.Boss:
                        // A boss is the one creature the map always knows about: the fight is the
                        // point of the area, and a pin to it is the direction of the quest.
                        item.Icon = MinimapIcon.Skull; item.Color = s.bossColor; item.Size = s.bossSize;
                        item.EdgePin = true; item.Halo = 0.55f; item.Reveal = MinimapReveal.Always; break;
                    case MinimapEntityKind.Ally:
                        item.Icon = MinimapIcon.Shield; item.Color = s.allyColor; item.Size = s.allySize;
                        item.Reveal = MinimapReveal.Always; break;
                    case MinimapEntityKind.Neutral:
                        item.Icon = MinimapIcon.Dot; item.Color = s.neutralColor; item.Size = s.neutralSize; break;
                    case MinimapEntityKind.Vendor:
                        item.Icon = c.VendorIcon; item.Color = s.vendorColor; item.Size = s.vendorSize;
                        item.Layer = MinimapLayer.Landmark; item.Halo = 0.25f; item.Reveal = MinimapReveal.Explored; break;
                    default:
                        continue;   // Hidden, or the player — the view draws the player itself.
                }
                Add(item);
            }
        }

        private CachedEntity Resolve(GameObject go, MinimapDotType hint, float time)
        {
            int id = go.GetInstanceID();
            if (_entityCache.TryGetValue(id, out var c) && time < c.Until) return c;
            c.Kind = MinimapEntityClassifier.Classify(go, hint);
            c.Name = MinimapEntityClassifier.DisplayName(go, c.Kind);
            c.VendorIcon = c.Kind == MinimapEntityKind.Vendor
                ? MinimapEntityClassifier.VendorIcon(go.GetComponent<VendorNPC>())
                : MinimapIcon.Coin;
            // Re-read twice a second: a charm, a death or a rank change must show up promptly,
            // but eight GetComponent calls per entity per frame buy nothing.
            c.Until = time + 0.5f;
            _entityCache[id] = c;
            if (_entityCache.Count > 512) _entityCache.Clear();
            return c;
        }

        private void CollectMarkers(MinimapStyle s)
        {
            var markers = MinimapManager.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null || !m.isActiveAndEnabled) continue;
                // A vendor is drawn from its entity dot with a trade icon; its marker would be
                // the same place drawn twice.
                if (m.GetComponent<VendorNPC>() != null) continue;

                var item = new MinimapItem { World = m.WorldPosition, Layer = MinimapLayer.Landmark, Key = m.GetInstanceID(), Reveal = MinimapReveal.Explored, Label = "Lugar" };
                if (m.GetComponent<ZonePortal>() != null)
                {
                    item.Icon = MinimapIcon.Portal; item.Color = s.portalColor; item.Size = s.portalSize; item.Halo = 0.5f;
                    item.Label = "Portal";
                }
                else if (m.GetComponent<InteriorExit>() != null)
                {
                    // Inside a room the way out is the most important thing on the map.
                    item.Icon = MinimapIcon.Door; item.Color = s.exitColor; item.Size = s.portalSize;
                    item.Halo = 0.55f; item.EdgePin = true; item.Reveal = MinimapReveal.Always;
                    item.Label = "Salida";
                }
                else if (m.GetComponent<BuildingDoor>() != null)
                {
                    item.Icon = MinimapIcon.Door; item.Color = s.doorColor; item.Size = s.doorSize;
                    item.Label = "Puerta";
                }
                else
                {
                    item.Icon = MinimapIcon.Diamond; item.Color = m.EffectiveColor; item.Size = Mathf.Max(8f, m.pixelSize * 2.2f);
                }
                Add(item);
            }
        }

        private void CollectBoard(MinimapStyle s)
        {
            var board = WorldMarkerBoard.All;
            for (int i = 0; i < board.Count; i++)
            {
                var wm = board[i];
                var item = new MinimapItem
                {
                    World = wm.Position,
                    Layer = MinimapLayer.Quest,
                    Size = s.questSize,
                    Bob = true,
                    Badge = true,
                    Key = KeyOf(wm),
                    Label = QuestLabel(wm),
                };
                switch (wm.Kind)
                {
                    case WorldMarkerKind.QuestOffer:
                        item.Icon = MinimapIcon.Exclaim; item.Color = s.questOfferColor; item.Halo = 0.35f; break;
                    case WorldMarkerKind.QuestTurnIn:
                        item.Icon = MinimapIcon.Question; item.Color = s.questTurnInColor; item.Halo = 0.55f;
                        item.EdgePin = true; item.Size = s.questSize * 1.1f; break;
                    default:
                        item.Icon = MinimapIcon.Star; item.Color = s.objectiveColor; item.Halo = 0.35f;
                        item.EdgePin = true; item.Badge = false; item.Size = s.questSize * 0.9f; break;
                }
                Add(item);
            }
        }

        private void CollectDeath(MinimapStyle s)
        {
            bool spirit = PlayerIsSpirit;
            var altars = ResurrectionAltarRegistry.Snapshot();
            for (int i = 0; i < altars.Count; i++)
            {
                var z = altars[i];
                if (z == null) continue;
                Add(new MinimapItem
                {
                    World = z.AnchorPoint,
                    Icon = MinimapIcon.Ankh,
                    Label = "Altar de resurrección",
                    Color = s.altarColor,
                    // While the player is a spirit the altar is the whole point of the map:
                    // bigger, glowing, and pinned to the rim wherever it is.
                    Size = spirit ? s.altarSize * 1.35f : s.altarSize,
                    Layer = MinimapLayer.Landmark,
                    Halo = spirit ? 0.9f : 0.2f,
                    EdgePin = spirit,
                    // A spirit knows every altar: the death system's rescue depends on it.
                    Reveal = spirit ? MinimapReveal.Always : MinimapReveal.Explored,
                    Key = z.GetInstanceID(),
                });
            }

            var corpse = _death != null ? _death.ActiveCorpse : null;
            if (corpse != null && corpse.isActiveAndEnabled)
            {
                Add(new MinimapItem
                {
                    World = corpse.transform.position,
                    Icon = MinimapIcon.Tomb,
                    Label = "Tu cuerpo",
                    Color = s.corpseColor,
                    Size = s.corpseSize,
                    Layer = MinimapLayer.Landmark,
                    Halo = 0.45f,
                    EdgePin = true,
                    Key = corpse.GetInstanceID(),
                });
            }
        }

        private void CollectWaypoint(MinimapStyle s)
        {
            if (!MinimapWaypoint.HasWaypoint) return;
            Add(new MinimapItem
            {
                World = MinimapWaypoint.Position,
                Icon = MinimapIcon.Pin,
                Label = "Tu destino",
                Color = s.waypointColor,
                Size = s.waypointSize,
                Layer = MinimapLayer.Quest,
                Halo = 0.6f,
                EdgePin = true,
                Bob = true,
                Key = 0x57A7 ^ MinimapWaypoint.Revision,
            });
        }

        private static string QuestLabel(WorldMarker wm)
        {
            string what = wm.Kind == WorldMarkerKind.QuestOffer ? "Misión disponible"
                        : wm.Kind == WorldMarkerKind.QuestTurnIn ? "Entregar misión"
                        : "Objetivo";
            return string.IsNullOrEmpty(wm.Label) ? what : what + " · " + wm.Label;
        }

        /// <summary>Stable key for a board marker: its kind and its position rounded to a tile.</summary>
        public static int KeyOf(WorldMarker wm)
        {
            unchecked
            {
                int h = (int)wm.Kind * 73856093;
                h ^= Mathf.RoundToInt(wm.Position.x) * 19349663;
                h ^= Mathf.RoundToInt(wm.Position.y) * 83492791;
                return h;
            }
        }
    }
}
