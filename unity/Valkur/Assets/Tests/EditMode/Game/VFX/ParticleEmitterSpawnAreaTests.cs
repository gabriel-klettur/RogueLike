using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers <c>ParticleVfxParams.spawnWidth</c> / <c>spawnHeight</c> (the F1 Properties
    /// panel's "Area Width" / "Area Height") going through <c>ParticleEmitter.ConfigureShape</c>'s
    /// <c>hasArea</c> branch.
    ///
    /// The box built there emits along its local +Z, so it has to be rotated to aim along the
    /// authored heading. The old rotation (<c>Quaternion.FromToRotation(Vector3.forward, headingDir)</c>)
    /// pins the box's local Z to the heading but leaves local Y — the authored HEIGHT axis — free
    /// to land wherever that rotation happens to put it, which for the default heading (up) is the
    /// world camera axis: Area Height authored invisible depth instead of visible extent, and the
    /// spawn read as a line no matter what was typed. The fix pins local Y to the camera axis
    /// directly (<c>Quaternion.LookRotation(headingDir, Vector3.forward)</c>), so local X carries
    /// the width (across the heading) and local Z carries the height (along the heading) — one of
    /// the two authored dimensions is never on camera depth, whatever the heading.
    ///
    /// These tests read geometry through the shape module itself (rotation + scale composed into
    /// world-space full extents and an emission-axis vector), not through hard-coded euler angles,
    /// so they hold regardless of which rotation formula the production code lands on.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterSpawnAreaTests
    {
        private const float CAMERA_AXIS_TOLERANCE = 0.02f; // 2x ParticleEmitter's BoxDepth const (0.01)

        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _createdAssets = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdObjects)
                if (go != null) Object.DestroyImmediate(go);
            _createdObjects.Clear();

            foreach (var asset in _createdAssets)
                if (asset != null) Object.DestroyImmediate(asset);
            _createdAssets.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ─────────────────────────────────────────────────────────────

        private ParticleEmitter CreateEmitter(string name = "SpawnAreaTestEmitter")
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        /// <summary>
        /// Builds a preset with an authored spawn area / heading. <paramref name="directionDegrees"/>
        /// stays at the field's own default (-1, "keep the kind's own behaviour") when not overridden.
        /// </summary>
        private ParticlePresetDefinition MakeAreaPreset(
            string id, string kind, float width, float height, float directionDegrees = -1f)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _createdAssets.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = kind;
            def.vfx = new ParticleVfxParams
            {
                kind             = kind,
                loops            = true,
                emitRate         = 20f,
                count            = 8,
                lifespan         = 0.25f,
                speed            = 1f,
                sizeMin          = 0.1f,
                sizeMax          = 0.3f,
                spawnWidth       = width,
                spawnHeight      = height,
                directionDegrees = directionDegrees,
            };
            return def;
        }

        private static ParticleSystem GetPs(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>(true);

        /// <summary>
        /// Sum of the absolute per-axis contribution of the shape's three local full-size axis
        /// vectors (right*scale.x, up*scale.y, forward*scale.z) once rotated into world space.
        /// This is geometry read through the shape module, not a re-derivation of the rotation
        /// formula under test — it stays valid whichever quaternion construction production code
        /// uses.
        /// </summary>
        private static Vector3 WorldExtents(ParticleSystem ps)
        {
            var shape = ps.shape;
            var q = Quaternion.Euler(shape.rotation);
            var s = shape.scale;
            Vector3 vx = q * (Vector3.right * s.x);
            Vector3 vy = q * (Vector3.up * s.y);
            Vector3 vz = q * (Vector3.forward * s.z);
            return new Vector3(
                Mathf.Abs(vx.x) + Mathf.Abs(vy.x) + Mathf.Abs(vz.x),
                Mathf.Abs(vx.y) + Mathf.Abs(vy.y) + Mathf.Abs(vz.y),
                Mathf.Abs(vx.z) + Mathf.Abs(vy.z) + Mathf.Abs(vz.z));
        }

        /// <summary>World-space direction the shape emits along (local +Z rotated into world space).</summary>
        private static Vector3 EmissionAxis(ParticleSystem ps)
            => Quaternion.Euler(ps.shape.rotation) * Vector3.forward;

        // ── Regression: height must land on screen, not on camera depth ────────────

        [Test]
        public void AreaOnly_HeightLandsOnScreenVertical_NotOnCameraDepth()
        {
            // This is the bug itself: before the fix, Area Height (16) ended up as ±8 units
            // of invisible Z depth and the visible vertical extent was the 0.01 depth hair.
            var emitter = CreateEmitter();
            var preset = MakeAreaPreset("area_only", "falling_leaf", width: 4f, height: 16f);

            emitter.ApplyPreset(preset, 1f);
            var extents = WorldExtents(GetPs(emitter));

            Assert.AreEqual(4f, extents.x, 1e-3f, "Area Width must read as horizontal extent.");
            Assert.AreEqual(16f, extents.y, 1e-3f,
                "Area Height must read as visible vertical extent — this was 0.01 before the fix.");
            Assert.LessOrEqual(extents.z, CAMERA_AXIS_TOLERANCE,
                "No authored dimension may land on the camera axis.");
        }

        [Test]
        public void AreaOnly_StillEmitsUpward()
        {
            // Fixing the height axis must not disturb the default heading — an area preset with
            // no authored direction has always sprayed straight up.
            var emitter = CreateEmitter();
            var preset = MakeAreaPreset("area_only_dir", "falling_leaf", width: 4f, height: 16f);

            emitter.ApplyPreset(preset, 1f);
            var axis = EmissionAxis(GetPs(emitter)).normalized;

            Assert.Greater(Vector3.Dot(axis, Vector3.up), 0.999f,
                "An area with no authored heading must default to emitting upward.");
        }

        [Test]
        public void AreaWithHeadingRight_HeightRunsAlongTheHeading()
        {
            // Area Height is ALONG the heading, Area Width ACROSS it — rotate the heading 90°
            // from the default (up → right) and the two authored numbers must swap screen axes.
            var emitter = CreateEmitter();
            var preset = MakeAreaPreset("area_right", "falling_leaf", width: 4f, height: 16f, directionDegrees: 0f);

            emitter.ApplyPreset(preset, 1f);
            var ps = GetPs(emitter);
            var extents = WorldExtents(ps);
            var axis = EmissionAxis(ps).normalized;

            Assert.AreEqual(16f, extents.x, 1e-2f, "Height (16) must run along a rightward heading.");
            Assert.AreEqual(4f, extents.y, 1e-2f, "Width (4) must run across a rightward heading.");
            Assert.LessOrEqual(extents.z, CAMERA_AXIS_TOLERANCE,
                "No authored dimension may land on the camera axis.");
            Assert.Greater(Vector3.Dot(axis, Vector3.right), 0.999f,
                "Sanity: directionDegrees 0 must aim the emission axis rightward.");
        }

        [Test]
        public void AreaWithDiagonalHeading_NeverPutsAnExtentOnTheCameraAxis()
        {
            // A diagonal heading is where a hand-built axis-flip rotation is most likely to leak
            // a component onto Z; the guarantee must hold for any heading, not just the cardinal ones.
            var emitter = CreateEmitter();
            const float DIRECTION_DEGREES = 225f;
            var preset = MakeAreaPreset("area_diagonal", "falling_leaf", width: 3f, height: 5f, directionDegrees: DIRECTION_DEGREES);

            emitter.ApplyPreset(preset, 1f);
            var ps = GetPs(emitter);
            var extents = WorldExtents(ps);
            var axis = EmissionAxis(ps).normalized;

            float rad = DIRECTION_DEGREES * Mathf.Deg2Rad;
            var expectedAxis = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);

            Assert.LessOrEqual(extents.z, CAMERA_AXIS_TOLERANCE,
                "A diagonal heading must not put either authored dimension on the camera axis.");
            Assert.Greater(Vector3.Dot(axis, expectedAxis), 0.999f,
                "Emission must still aim exactly along the authored diagonal heading.");
        }

        [Test]
        public void InstanceScale_ScalesBothAuthoredExtents()
        {
            // ApplyPreset(preset, scaleMultiplier) scales every world-unit field; both authored
            // spawn-area dimensions must follow it, same as every other spatial parameter does.
            var emitter = CreateEmitter();
            var preset = MakeAreaPreset("area_scaled", "falling_leaf", width: 4f, height: 16f);

            emitter.ApplyPreset(preset, 2f);
            var extents = WorldExtents(GetPs(emitter));

            Assert.AreEqual(8f, extents.x, 1e-2f, "Width must scale with the instance multiplier.");
            Assert.AreEqual(32f, extents.y, 1e-2f, "Height must scale with the instance multiplier.");
        }

        [Test]
        public void AreaOff_KeepsTheKindsOwnShape()
        {
            // spawnWidth/spawnHeight at 0 must leave the override path untouched — falling_leaf's
            // hard-coded 2-unit strip is exactly what the "Authored overrides" comment says is
            // otherwise preserved.
            var emitter = CreateEmitter();
            var preset = MakeAreaPreset("area_off", "falling_leaf", width: 0f, height: 0f);

            emitter.ApplyPreset(preset, 1f);
            var shape = GetPs(emitter).shape;

            Assert.AreEqual(ParticleSystemShapeType.Box, shape.shapeType);

            // The strip is now written in the SAME terms as the authored box: width on local X,
            // hair-thin depth on local Y, height on local Z, aimed upward. It used to be
            // (2, 0.1, 0.1) unrotated, and the difference mattered the moment a per-instance
            // resize materialised the strip into an authored box — the shape jumped from one
            // convention to the other on the first pixel of the drag, taking the direction of
            // `speed` with it. Same footprint on screen, one description of it.
            Assert.AreEqual(2f, shape.scale.x, 1e-4f, "The strip is still two units wide.");
            Assert.AreEqual(0.1f, shape.scale.z, 1e-4f, "And a tenth of a unit tall.");
            Assert.Less(shape.scale.y, 0.05f,
                "Its depth lies on the camera axis and stays hair-thin.");
            Assert.AreEqual(BoxRotationForUp(), shape.rotation,
                "Aimed the way the authored-box path aims it, so materialising the strip " +
                "reproduces it exactly.");
        }

        /// <summary>The emitter's own BoxRotationFor(90) — local +Z up, local +Y on world Z.</summary>
        private static Vector3 BoxRotationForUp()
            => Quaternion.LookRotation(Vector3.up, Vector3.forward).eulerAngles;
    }
}
