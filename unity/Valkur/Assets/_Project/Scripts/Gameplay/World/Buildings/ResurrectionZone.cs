using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Component bolted onto a resurrection altar building. Polls
    /// <see cref="EntityRegistry.Player"/> every <see cref="CheckInterval"/>; if the player is in
    /// spirit form and standing inside this altar's ZONE, calls
    /// <see cref="DeathSequenceController.Revive"/>.
    ///
    /// <para>The zone's shape is the template's <see cref="ResurrectionAnchor"/> — the whole
    /// building plus a reach, or its base, its middle, or its top. It is computed by
    /// <see cref="ResurrectionZoneGeometry"/>, which is pure and therefore testable without a
    /// scene: an altar zone in the wrong place looks exactly like one the player has not reached,
    /// so it has to be provable rather than observable.</para>
    ///
    /// <para>We poll instead of using <c>OnTriggerEnter2D</c> because (a) we want to resurrect
    /// even if the player was already inside the footprint at the moment of death; (b) the
    /// spirit's collider excludes several layers and a trigger is not guaranteed to fire across
    /// that switch; and (c) buildings carry no <c>Rigidbody2D</c>, so a trigger would depend
    /// entirely on the player's Dynamic body — which goes to sleep after 0.5 s at rest, and a
    /// sleeping body starts no new contacts. <c>BuildingDoor</c> records the same reasoning.</para>
    ///
    /// <para><b>The padding is not cosmetic.</b> With none, the spirit has to stand on the exact
    /// rectangle, which for a narrow arch is roughly a tile — reachable, but only by a player who
    /// already knows where the edge is. It is a fraction of a tile of slack given to every shape,
    /// not a second activation radius; the reach that IS a radius belongs to Proximity alone.</para>
    ///
    /// <para><b>Base, Center and Top are inside the building, so a SOLID one can lock them out.</b>
    /// The collision grid decides where the player may stand, and it knows nothing about altars —
    /// an arch works because its opening is painted walkable. Proximity is the anchor that is
    /// always reachable, which is why it is the default and why the authoring surfaces say so.</para>
    ///
    /// <para>Bound automatically by <see cref="ResurrectionZoneAutoBinder"/> through
    /// <see cref="ResurrectionAltarRegistry.TryBind"/>, which is the only place that knows which
    /// templates are altars.</para>
    /// </summary>
    [RequireComponent(typeof(BuildingObject))]
    public class ResurrectionZone : MonoBehaviour
    {
        /// <summary>
        /// Seconds between footprint checks. A constant rather than a serialized field because
        /// this component is added by code and has no inspector to be authored from — the
        /// unreachable-field defect this subsystem already shipped twice.
        /// </summary>
        private const float CheckInterval = 0.1f;

        private BuildingObject _building;
        private float _timer;
        private bool _spiritInside;

        /// <summary>The building this altar is bolted to. Null before Awake.</summary>
        public BuildingObject Building => _building;

        /// <summary>
        /// The point every distance measurement uses: the footprint's centre when the building
        /// can report one, else the transform. A building's pivot sits at a corner of a footprint
        /// several tiles across, so ranking two altars on the transform alone can pick the
        /// further one.
        /// </summary>
        public Vector2 AnchorPoint
        {
            get
            {
                // The centre of the ZONE, not of the building. The trail leads the player here and
                // the zone is what revives them: aiming at the building's middle while the altar
                // only accepts its base would walk the spirit to a spot that does nothing, which is
                // the same class of lie as the straight-line path through walls it cannot cross.
                if (TryGetZoneRect(out Rect zone)) return zone.center;

                if (_building == null) _building = GetComponent<BuildingObject>();
                if (_building != null && _building.TryGetWorldRect(out Rect rect)) return rect.center;
                return transform.position;
            }
        }

        /// <summary>
        /// The anchor this altar uses — where on the building the spirit has to stand.
        ///
        /// <para>Read from the TEMPLATE on every call rather than cached, so an author flipping it
        /// in the Death editor or from the console sees it on the next poll instead of on the next
        /// world load. It is one enum read off an object already in hand.</para>
        ///
        /// <para>A template bound only through the legacy <c>altarTemplateIds</c> bridge answers
        /// <see cref="ResurrectionAnchor.Proximity"/>, which is the shape the list-based altars
        /// always had — a padded rect on every side.</para>
        /// </summary>
        public ResurrectionAnchor Anchor
        {
            get
            {
                var t = Template;
                return t != null ? t.resurrectionAnchor : ResurrectionAnchor.Proximity;
            }
        }

        /// <summary>The Proximity reach in world units, or the template default when there is none.</summary>
        public float Radius
        {
            get
            {
                var t = Template;
                return t != null ? Mathf.Max(0.25f, t.resurrectionRadius) : 1.5f;
            }
        }

        private BuildingTemplateData Template
        {
            get
            {
                if (_building == null) _building = GetComponent<BuildingObject>();
                return _building != null ? _building.Template : null;
            }
        }

        /// <summary>
        /// The zone as a world rect — exactly what <see cref="Contains"/> tests, so a marker drawn
        /// from this cannot promise a spot the runtime refuses.
        /// </summary>
        public bool TryGetZoneRect(out Rect zone)
        {
            zone = default;
            if (_building == null) _building = GetComponent<BuildingObject>();
            if (_building == null || !_building.TryGetWorldRect(out Rect rect)) return false;

            zone = ResurrectionZoneGeometry.ZoneOf(
                rect, Anchor, Radius, DeathTuning.Active.altarActivationPadding);
            return true;
        }

        /// <summary>True when <paramref name="worldPos"/> is inside this altar's zone.</summary>
        public bool Contains(Vector2 worldPos)
        {
            return TryGetZoneRect(out Rect zone) && zone.Contains(worldPos);
        }

        private void Awake()
        {
            _building = GetComponent<BuildingObject>();
        }

        private void OnEnable()
        {
            ResurrectionAltarRegistry.Register(this);
        }

        /// <summary>
        /// Registration survives being deactivated — only destruction removes it. Membership is
        /// not eligibility: <c>Snapshot</c> and <c>TryGetNearest</c> filter on
        /// <c>isActiveAndEnabled</c>, and unregistering here would need a second mechanism to put
        /// the altar back, which is the defect <c>AlliedUnit</c> already paid for.
        /// </summary>
        private void OnDestroy()
        {
            ResurrectionAltarRegistry.Unregister(this);
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < CheckInterval) return;
            _timer = 0f;

            var player = EntityRegistry.Player;
            if (player == null) { _spiritInside = false; return; }

            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit == null || !spirit.IsSpirit) { _spiritInside = false; return; }

            Vector3 pos = player.transform.position;
            bool inside = Contains(new Vector2(pos.x, pos.y));

            if (inside && !_spiritInside)
            {
                _spiritInside = true;

                var controller = ServiceLocator.Get<DeathSequenceController>();
                if (controller != null) controller.Revive();
                else Debug.LogWarning("[ResurrectionZone] DeathSequenceController not registered in ServiceLocator.");
            }
            else if (!inside && _spiritInside)
            {
                _spiritInside = false;
            }
        }
    }
}
