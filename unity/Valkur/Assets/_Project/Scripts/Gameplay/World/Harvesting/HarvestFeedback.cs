using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What a blow on a node LOOKS like. Attached the first time the node is worked, and holding
    /// no rigs of its own: every particle and flash comes from the shared <see cref="HarvestFx"/>,
    /// so working a hundred nodes costs three particle systems, not a hundred.
    ///
    /// <para><b>THE EVENT IS THE DESIGN.</b> Chips fly off the struck side, a few leaves shake loose
    /// from the crown, the trunk shudders (<see cref="BuildingHitShake"/>, driven by durability so
    /// every source gets it), the camera nudges, and a yield pops its item's name over the worker.
    /// Continuous motion at a steady rate stops being read after about a second; only an EVENT
    /// resets it.</para>
    ///
    /// <para><b>ONE PATH FOR EVERY ROUTE IN.</b> It listens to <see cref="HarvestNode.BlowLanded"/>,
    /// which a Destroy node raises for every blow durability receives. A tree chopped with the
    /// attack button used to show nothing at all until it disappeared.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestFeedback : MonoBehaviour
    {
        private const int CHIPS_PER_BLOW = 7;
        private const int LEAVES_PER_BLOW = 3;

        private static readonly Color WrongToolColor = new Color(0.78f, 0.80f, 0.86f, 1f);
        private static readonly Color BonusColor = new Color(1f, 0.86f, 0.42f, 1f);

        /// <summary>
        /// Yields arriving this close together are announced as ONE line. A finishing blow pays
        /// its own yield and the fell bonus in the same frame, and a master's axe can free two
        /// yields a blow — measured on the first capture, four "+1 Ramas secas" drawn on top of
        /// each other into an unreadable smear.
        /// </summary>
        private const float COALESCE_SECONDS = 0.35f;

        private HarvestNode _node;
        private bool _warnedWrongTool;

        private int _pendingTotal;
        private string _pendingBestName = string.Empty;
        private Color _pendingBestColour = Color.white;
        private int _pendingBestRank = -1;
        private bool _pendingSeveralKinds;
        private int _flightsThisBeat;
        private float _beatStartedAt = -1f;
        private bool _warnedFullBag;
        private float _flushAt;

        /// <summary>Blows this component has drawn. A test seam.</summary>
        public int DrawnBlows { get; private set; }

        /// <summary>The most recent floating line. A test seam, so the wording can be pinned.</summary>
        public string LastMessage { get; private set; }

        /// <summary>Attach (or find) the feedback for a node. Idempotent.</summary>
        public static HarvestFeedback Attach(HarvestNode node)
        {
            if (node == null) return null;

            var existing = node.GetComponent<HarvestFeedback>();
            if (existing != null) return existing;

            var feedback = node.gameObject.AddComponent<HarvestFeedback>();
            feedback.Bind(node);
            return feedback;
        }

        private void Bind(HarvestNode node)
        {
            enabled = false;
            _node = node;
            _node.BlowLanded += OnBlowLanded;
            _node.Depleted += OnDepleted;
            _node.Regrown += OnRegrown;
            _node.Felled += OnFelled;
            _node.ItemYielded += OnItemYielded;
        }

        private void OnDestroy()
        {
            if (_node == null) return;
            _node.BlowLanded -= OnBlowLanded;
            _node.Depleted -= OnDepleted;
            _node.Regrown -= OnRegrown;
            _node.Felled -= OnFelled;
            _node.ItemYielded -= OnItemYielded;
        }

        // Beats ------------------------------------------------------------------------

        private void OnBlowLanded(HarvestBlow blow, int yields)
        {
            DrawnBlows++;

            Vector3 contact = ContactPoint();
            Vector2 away = AwayFromWorker(contact);
            var profile = _node != null ? _node.Profile : null;

            HarvestFx.Chips(contact, ChipColor(profile), blow.Immune ? 2 : CHIPS_PER_BLOW, away);
            HarvestFx.Flash(contact, blow.Immune
                ? new Color(0.70f, 0.78f, 0.95f, 0.55f)
                : new Color(1f, 0.92f, 0.68f, 0.8f), 0.5f);

            if (!blow.Immune && profile != null && profile.material == MaterialClass.Wood)
                ShakeLeaves(profile, LEAVES_PER_BLOW);

            PlayBlowSound(blow);

            // A bounced blow gets a smaller camera beat than a productive one: the camera is the
            // fastest channel the player reads and must not say the swing worked when it did not.
            CameraFeel.Cue(blow.Immune ? CameraFeelCue.AttackWhiff : CameraFeelCue.ImpactLight, away);

            if (blow.Immune || blow.WrongTool) ReportWrongTool(contact, blow);
            // A skilled node names each item through ItemYielded; only the legacy pool path, which
            // does not know what it dropped, is reported by count here.
            if (yields > 0 && (profile == null || !profile.UsesSkillYield)) ReportYield(yields);
        }

        /// <summary>
        /// A yield names WHAT dropped, in its rarity's colour, over the worker's head — the place
        /// the player is looking and where the stack actually lands. "+1" on its own could not say
        /// that the skill had just turned up the first birch log.
        /// </summary>
        /// <summary>
        /// A log was extracted: launch its icon from the trunk to the worker. Yields that arrive
        /// together (a finishing blow plus the fell bonus) leave the tree one after another,
        /// staggered, so each one is seen rather than five icons stacked into one.
        /// </summary>
        private void OnItemYielded(ItemDefinition item, bool inBag)
        {
            if (Time.time - _beatStartedAt > 0.3f)
            {
                _beatStartedAt = Time.time;
                _flightsThisBeat = 0;
            }

            Vector3 from = FlightOrigin();
            float delay = _flightsThisBeat * 0.09f;
            _flightsThisBeat++;

            var worker = _node != null ? _node.LastWorker : null;
            HarvestYieldFlight.Launch(item, from, worker, inBag, delay, OnFlightLanded);
        }

        private void OnFlightLanded(ItemDefinition item, bool inBag)
        {
            if (inBag)
            {
                ReportYield(1, item);
                return;
            }

            if (_warnedFullBag) return;
            _warnedFullBag = true;
            Say(WorkerHead(), "Mochila llena", WrongToolColor);
        }

        /// <summary>Where a log leaves the tree: the struck trunk, a little above its middle.</summary>
        private Vector3 FlightOrigin()
        {
            if (_node == null) return transform.position;
            var b = _node.InteractionBounds;
            return new Vector3(b.center.x, b.center.y + b.extents.y * 0.4f, 0f);
        }

        private void ReportYield(int yields) => ReportYield(yields, null);

        private void ReportYield(int yields, ItemDefinition item)
        {
            string name = item != null && !string.IsNullOrEmpty(item.displayName) ? item.displayName : string.Empty;

            // The line names the MOST VALUABLE thing in the batch: that is the news. "+5 Madera
            // común y más" hides the first birch log a player has ever found; "+5 Madera de abedul
            // y más" is the moment the skill paid off.
            int rank = item != null ? (int)item.rarity * 1000 + item.buyPrice : 0;
            if (_pendingTotal > 0 && name != _pendingBestName) _pendingSeveralKinds = true;
            if (rank > _pendingBestRank)
            {
                _pendingBestRank = rank;
                _pendingBestName = name;
                _pendingBestColour = item != null ? RarityPalette.Color(item.rarity) : BonusColor;
            }
            _pendingTotal += yields;

            Schedule();
        }

        /// <summary>
        /// The felling itself says nothing in words: the crown coming down is the announcement,
        /// and the only text the activity puts on screen is what was extracted.
        /// </summary>
        private void OnFelled(int bonus)
        {
            var profile = _node != null ? _node.Profile : null;
            if (profile != null) ShakeLeaves(profile, 14);
        }

        private void Schedule()
        {
            _flushAt = Time.time + COALESCE_SECONDS;
            enabled = true;
        }

        private void Update()
        {
            if (_pendingTotal == 0) { enabled = false; return; }
            if (Time.time < _flushAt) return;
            Flush();
        }

        /// <summary>
        /// Announce what has landed as ONE line over the worker: the total, named after the most
        /// valuable item. A fixture calls this directly: Edit Mode has no Update.
        /// </summary>
        public void Flush()
        {
            if (_pendingTotal > 0)
            {
                string name = string.IsNullOrEmpty(_pendingBestName) ? string.Empty : " " + _pendingBestName;
                if (_pendingSeveralKinds) name += " y más";
                Say(WorkerHead(), "+" + _pendingTotal + name, _pendingBestColour);
            }

            _pendingTotal = 0;
            _pendingBestName = string.Empty;
            _pendingBestRank = -1;
            _pendingSeveralKinds = false;
        }

        /// <summary>
        /// Said ONCE per node life. A wrong tool is a standing condition, not an event: saying it
        /// on every blow would put a line of text on screen twice a second.
        /// </summary>
        private void ReportWrongTool(Vector3 contact, HarvestBlow blow)
        {
            if (_warnedWrongTool) return;
            _warnedWrongTool = true;
            Say(contact + Vector3.up * 0.35f, blow.Immune ? "Sin efecto" : "Herramienta inadecuada", WrongToolColor);
        }

        private void OnDepleted()
        {
            _warnedWrongTool = false;
            _warnedFullBag = false;
            if (_node != null && _node.Mode == HarvestMode.Deplete)
                HarvestFx.Chips(ContactPoint(), ChipColor(_node.Profile), CHIPS_PER_BLOW * 3, Vector2.up);
        }

        private void OnRegrown() => _warnedWrongTool = false;

        private void Say(Vector3 at, string text, Color color)
        {
            LastMessage = text;
            FloatingDamageSpawner.ShowAt(at, text, color);
        }

        // Geometry ---------------------------------------------------------------------

        /// <summary>Where the blow lands: the point of the trunk nearest the worker, at chest height.</summary>
        private Vector3 ContactPoint()
        {
            var bounds = _node != null ? _node.InteractionBounds : new Bounds(transform.position, Vector3.zero);
            var worker = _node != null ? _node.LastWorker : null;
            if (worker == null) return new Vector3(bounds.center.x, bounds.center.y, 0f);

            Vector3 p = bounds.ClosestPoint(worker.transform.position);
            return new Vector3(p.x, Mathf.Max(p.y, bounds.center.y), 0f);
        }

        private Vector2 AwayFromWorker(Vector3 contact)
        {
            var worker = _node != null ? _node.LastWorker : null;
            if (worker == null) return Vector2.up;
            Vector2 d = (Vector2)(contact - worker.transform.position);
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up;
        }

        private Vector3 WorkerHead()
        {
            var worker = _node != null ? _node.LastWorker : null;
            if (worker == null) return ContactPoint() + Vector3.up * 0.5f;
            var sr = worker.GetComponentInChildren<SpriteRenderer>();
            float top = sr != null && sr.sprite != null ? sr.bounds.max.y : worker.transform.position.y + 1.8f;
            return new Vector3(worker.transform.position.x, top + 0.2f, 0f);
        }

        private void ShakeLeaves(DestructionProfile profile, int count)
        {
            var canopy = _node != null && _node.Building != null ? _node.Building.CanopyRenderer : null;
            if (canopy == null || !canopy.enabled || !canopy.gameObject.activeInHierarchy) return;
            var b = canopy.bounds;
            HarvestFx.Leaves(new Vector3(b.center.x, b.center.y - b.extents.y * 0.2f, 0f),
                profile.leafColor, count, b.extents.x * 0.7f);
        }

        /// <summary>
        /// Chips take their colour from the MATERIAL that was struck, not from the tool — the one
        /// property of the blow the player can already see.
        /// </summary>
        private static Color ChipColor(DestructionProfile profile)
        {
            var material = profile != null ? profile.material : MaterialClass.Stone;
            switch (material)
            {
                case MaterialClass.Wood:    return new Color(0.62f, 0.42f, 0.22f, 1f);
                case MaterialClass.Foliage: return new Color(0.33f, 0.55f, 0.24f, 1f);
                case MaterialClass.Metal:   return new Color(0.68f, 0.70f, 0.76f, 1f);
                case MaterialClass.Cloth:   return new Color(0.78f, 0.72f, 0.60f, 1f);
                case MaterialClass.Glass:   return new Color(0.72f, 0.88f, 0.94f, 1f);
                default:                    return new Color(0.56f, 0.55f, 0.53f, 1f);
            }
        }

        /// <summary>
        /// Gated on <c>HasSfx</c>, never called blind: the catalog ships no harvest sounds yet, and
        /// <c>PlaySfxById</c> warns once per unresolved id. When they exist, these light up.
        /// </summary>
        private void PlayBlowSound(HarvestBlow blow)
        {
            if (!ServiceLocator.TryGet<IAudioService>(out var audio) || audio == null) return;

            string material = _node != null && _node.Profile != null
                ? _node.Profile.material.ToString().ToLowerInvariant()
                : "stone";

            string id = blow.Immune ? $"harvest_{material}_bounce" : $"harvest_{material}_hit";
            if (audio.HasSfx(id)) audio.PlaySfxById(id);
        }
    }
}
