using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Feel;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What a blow LOOKS and SOUNDS like. Attached to a node the first time it is worked, so
    /// a forest of hundreds of trees carries nothing at all until someone swings at one.
    ///
    /// <para>THE EVENT IS THE DESIGN. A shift is a stationary character beside a stationary
    /// rock; everything that makes it readable is the beat. Chips fly, the node is lit for a
    /// few frames, the camera nudges, and every so often a stack of ore pops out with its own
    /// number over it. Take the beats away and what is left is a progress bar, which is the
    /// same finding the vortex recorded: continuous motion at a steady rate stops being read
    /// after about a second, and only an EVENT resets it.</para>
    ///
    /// <para>The flash is an ADDITIVE overlay sprite rather than a tint on the building.
    /// <see cref="HarvestNode"/> owns the node colour (it multiplies the spent tint over the
    /// pristine one), and a second writer that captured "the original" during a flash would
    /// record the flash as the baseline — exactly the bug <c>SpriteTintStack</c> exists to
    /// prevent on entities. An overlay has no such conversation with anyone.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestFeedback : MonoBehaviour
    {
        /// <summary>Seconds the impact spark stays up. Short: it is a beat, not a light.</summary>
        private const float SPARK_SECONDS = 0.12f;

        /// <summary>Impact spark diameter in world units, roughly two 32-PPU texels.</summary>
        private const float SPARK_SIZE = 0.55f;

        private const int CHIPS_PER_BLOW = 7;

        private static readonly Color YieldTextColor = new Color(1f, 0.86f, 0.42f, 1f);
        private static readonly Color WrongToolColor = new Color(0.78f, 0.80f, 0.86f, 1f);

        private HarvestNode _node;
        private ParticleSystem _chips;
        private SpriteRenderer _spark;
        private float _sparkUntil;
        private bool _warnedWrongTool;

        /// <summary>Blows this component has drawn. A test seam.</summary>
        public int DrawnBlows { get; private set; }

        /// <summary>
        /// Attach (or find) the feedback for a node. Idempotent, so a session that restarts
        /// does not stack two of them.
        /// </summary>
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
            _node = node;
            _node.BlowLanded += OnBlowLanded;
            _node.Depleted += OnDepleted;
            _node.Regrown += OnRegrown;
        }

        private void OnDestroy()
        {
            if (_node == null) return;
            _node.BlowLanded -= OnBlowLanded;
            _node.Depleted -= OnDepleted;
            _node.Regrown -= OnRegrown;
        }

        private void LateUpdate()
        {
            if (_spark == null || !_spark.enabled) return;
            if (Time.time < _sparkUntil) return;
            _spark.enabled = false;
        }

        // Beats ------------------------------------------------------------------------

        private void OnBlowLanded(HarvestBlow blow, int yields)
        {
            DrawnBlows++;

            Vector3 contact = ContactPoint();

            EmitChips(contact, blow);
            FlashSpark(contact, blow);
            PlayBlowSound(blow);

            // A bounced blow gets a smaller camera beat than a productive one, because the
            // camera is the fastest channel the player reads and it should not tell them the
            // swing worked when it did not.
            CameraFeel.Cue(blow.Immune ? CameraFeelCue.AttackWhiff : CameraFeelCue.ImpactLight,
                (contact - transform.position));

            if (blow.Immune || blow.WrongTool) ReportWrongTool(contact, blow);
            if (yields > 0) ReportYield(contact, yields);
        }

        /// <summary>
        /// A yield is the one moment the whole activity exists for, so it gets its own
        /// number. The count is what the drop resolver actually spawned, not what was rolled,
        /// so the text can never promise a stack the player will not find on the ground.
        /// </summary>
        private void ReportYield(Vector3 contact, int yields)
        {
            FloatingDamageSpawner.ShowAt(contact + Vector3.up * 0.35f,
                yields > 1 ? $"+{yields}" : "+1", YieldTextColor);
        }

        /// <summary>
        /// Said ONCE per session. A wrong tool is a standing condition, not an event: saying
        /// it on every blow would put a line of text on screen twice a second for as long as
        /// the player keeps trying, which reads as a bug rather than as advice.
        /// </summary>
        private void ReportWrongTool(Vector3 contact, HarvestBlow blow)
        {
            if (_warnedWrongTool) return;
            _warnedWrongTool = true;

            string message = blow.Immune ? "Immune" : "Wrong tool";
            FloatingDamageSpawner.ShowAt(contact + Vector3.up * 0.35f, message, WrongToolColor);
        }

        private void OnDepleted()
        {
            _warnedWrongTool = false;
            if (_chips == null) return;

            // A final, bigger burst so the node running out is an event of its own rather
            // than the blows merely stopping.
            _chips.Emit(CHIPS_PER_BLOW * 3);
        }

        private void OnRegrown()
        {
            _warnedWrongTool = false;
        }

        // Rigs -------------------------------------------------------------------------

        private Vector3 ContactPoint()
        {
            var bounds = _node != null ? _node.InteractionBounds : new Bounds(transform.position, Vector3.zero);
            return new Vector3(bounds.center.x, bounds.center.y, 0f);
        }

        /// <summary>
        /// The chips are the only OPAQUE layer here, and deliberately so. Everything else is
        /// light; opaque debris is what says the world is being affected rather than merely
        /// lit, which is the same split <c>KiAuraFX</c> and <c>VortexFunnelFX</c> both record.
        /// </summary>
        private void EmitChips(Vector3 contact, HarvestBlow blow)
        {
            EnsureChips();
            if (_chips == null) return;

            _chips.transform.position = contact;

            var main = _chips.main;
            main.startColor = ChipColor(blow);

            _chips.Emit(blow.Immune ? 2 : CHIPS_PER_BLOW);
        }

        private void EnsureChips()
        {
            if (_chips != null) return;

            var go = new GameObject("HarvestChips");
            go.transform.SetParent(transform, worldPositionStays: false);

            var ps = go.AddComponent<ParticleSystem>();

            // AddComponent starts the system immediately (playOnAwake defaults true), and
            // main.duration cannot be written while it plays: configuring inline fires
            // "Setting the duration while system is still playing is not supported" and
            // silently keeps the old value. Stop, configure, play.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.gravityModifier = 2.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 96;

            var emission = ps.emission;
            emission.enabled = false; // Emission is by explicit Emit(), one burst per blow.

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 10;
            // Opaque, not additive: a dark chip on an additive surface adds almost nothing and
            // the layer would vanish with nothing failing.
            renderer.sharedMaterial = ParticleMaterialCache.Get(Texture2D.whiteTexture, additive: false);

            ps.Play();
            _chips = ps;
        }

        /// <summary>
        /// Chips take their colour from the MATERIAL that was struck, not from the tool. It is
        /// the one property of the blow the player can already see, so matching it is what
        /// makes the debris read as coming out of the rock rather than off the pick.
        /// </summary>
        private Color ChipColor(HarvestBlow blow)
        {
            var material = _node != null && _node.Profile != null
                ? _node.Profile.material
                : MaterialClass.Stone;

            switch (material)
            {
                case MaterialClass.Wood:    return new Color(0.55f, 0.36f, 0.18f, 1f);
                case MaterialClass.Foliage: return new Color(0.33f, 0.55f, 0.24f, 1f);
                case MaterialClass.Metal:   return new Color(0.68f, 0.70f, 0.76f, 1f);
                case MaterialClass.Cloth:   return new Color(0.78f, 0.72f, 0.60f, 1f);
                case MaterialClass.Glass:   return new Color(0.72f, 0.88f, 0.94f, 1f);
                default:                    return new Color(0.56f, 0.55f, 0.53f, 1f);
            }
        }

        private void FlashSpark(Vector3 contact, HarvestBlow blow)
        {
            EnsureSpark();
            if (_spark == null) return;

            _spark.transform.position = contact;
            _spark.color = blow.Immune
                ? new Color(0.70f, 0.78f, 0.95f, 0.55f)
                : new Color(1f, 0.92f, 0.68f, 0.85f);
            _spark.enabled = true;
            _sparkUntil = Time.time + SPARK_SECONDS;
        }

        private void EnsureSpark()
        {
            if (_spark != null) return;

            var go = new GameObject("HarvestSpark");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localScale = Vector3.one * SPARK_SIZE;

            _spark = go.AddComponent<SpriteRenderer>();
            _spark.sprite = ElementalSprites.HotCore;
            _spark.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            _spark.sortingLayerName = SortingConfig.LAYER_VFX;
            _spark.sortingOrder = 11;
            _spark.enabled = false;
        }

        /// <summary>
        /// Gated on <c>HasSfx</c>, never called blind. <c>PlaySfxById</c> warns once per
        /// unresolved id BY DESIGN — an explicit id that fails to resolve is a data bug — and
        /// this project requires a clean console, so a speculative id would push a warning
        /// into it on the first swing of every session. The catalog ships no harvest sounds
        /// yet; when it does, these ids light up with no code change.
        /// </summary>
        private void PlayBlowSound(HarvestBlow blow)
        {
            if (!ServiceLocator.TryGet<IAudioService>(out var audio) || audio == null) return;

            string material = _node != null && _node.Profile != null
                ? _node.Profile.material.ToString().ToLowerInvariant()
                : "stone";

            string id = blow.Immune
                ? $"harvest_{material}_bounce"
                : $"harvest_{material}_hit";

            if (audio.HasSfx(id)) audio.PlaySfxById(id);
        }
    }
}
