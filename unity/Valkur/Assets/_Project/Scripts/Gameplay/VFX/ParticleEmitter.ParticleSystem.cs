using System.Collections;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleEmitter
    {
        // ------------------------------------------------------------------ particle system

        private void EnsureParticleSystem()
        {
            if (_ps != null) return;
            // Once composite presets exist, a reused emitter can carry LAYER
            // ParticleSystems too (see ParticleEmitter.Layers.cs), so a generic
            // GetComponentInChildren could just as easily return a layer's system as
            // the root's. The root is always the child named "Particles" (see the
            // creation branch below) — look for that by name first, and only fall
            // back to the old any-ParticleSystem search when it is not there yet
            // (fresh emitter, or a save that predates layers).
            var rootChild = transform.Find("Particles");
            _ps = rootChild != null ? rootChild.GetComponent<ParticleSystem>() : null;
            // includeInactive: a burst whose stopAction disabled the child would
            // otherwise look absent here, and we would build a second one beside it.
            if (_ps == null)
                _ps = GetComponentInChildren<ParticleSystem>(true);
            if (_ps == null)
            {
                var child = new GameObject("Particles");
                child.transform.SetParent(transform, false);
                _ps = child.AddComponent<ParticleSystem>();
                // AddComponent starts the system there and then. Clearing playOnAwake
                // afterwards only affects the NEXT awake, so the system is still running
                // while it is being configured — and Unity rejects several main-module
                // writes on a playing system. Stop it outright.
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var childMain = _ps.main;
                childMain.playOnAwake = false;
            }
        }

        /// <summary>
        /// Configures one ParticleSystem — either the root <c>_ps</c> or a layer child
        /// built by <see cref="SyncLayers"/> — from a <see cref="ParticleVfxParams"/>
        /// block. Takes the target explicitly rather than reading the <c>_ps</c> field
        /// so the exact same recipe (every module, every leak-guard) applies to both;
        /// duplicating this method per-layer would drift the two paths apart the first
        /// time either one was tuned.
        /// </summary>
        private void ConfigureParticleSystem(ParticleSystem ps, ParticleVfxParams p, float scale)
        {
            string kind = p.kind ?? "";
            // Use the explicit loops attribute as the single source of truth.
            // loops=false → finite one-shot burst (explosion, smoke_burst, slash, firework by default).
            // loops=true  → continuous emitter that never self-disables.
            bool isBurst = !p.loops;
            bool isBurstLoop = IsBurstWithInterval(kind);

            float lifeSec = Mathf.Max(0.05f, p.lifespan);

            // ---- Main ----
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = p.loops;
            main.stopAction = isBurst
                ? ParticleSystemStopAction.Disable
                : ParticleSystemStopAction.None;
            main.startLifetime = lifeSec;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, p.speed * scale);
            // sizeMin/sizeMax are the HEIGHT; sizeAspect scales width against it. At 1 this
            // takes the uniform path every preset was authored against.
            float aspect = p.sizeAspect > 0f ? p.sizeAspect : 1f;
            if (Mathf.Abs(aspect - 1f) > 0.001f)
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(p.sizeMin * scale * aspect,
                                                                 p.sizeMax * scale * aspect);
                main.startSizeY = new ParticleSystem.MinMaxCurve(p.sizeMin * scale, p.sizeMax * scale);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(p.sizeMin * scale, p.sizeMax * scale);
            }
            else
            {
                // Emitters are reused across presets — leaving startSize3D on would make the
                // next preset inherit this one's aspect.
                main.startSize3D = false;
                main.startSize = new ParticleSystem.MinMaxCurve(p.sizeMin * scale, p.sizeMax * scale);
            }
            main.startColor = BuildColorParameter(p);
            // Gravity: scalar gravity means falling down; vector gravity is pre-converted
            if (p.useGravityVector)
            {
                // Use velocity-over-lifetime for arbitrary gravity direction
                main.gravityModifier = 0f;
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(p.gravityVector.x);
                vel.y = new ParticleSystem.MinMaxCurve(p.gravityVector.y);
            }
            else
            {
                main.gravityModifier = p.gravity > 0f ? p.gravity / UNITY_GRAVITY : 0f;
                var vel = ps.velocityOverLifetime;
                vel.enabled = false;
            }
            // World space is what makes a trail a trail. In local space every particle is
            // carried along by the emitter, so a projectile moving at 16 u/s drags its
            // whole "wake" with it and leaves nothing behind — the effect reads as a rigid
            // blob rather than as something travelling. "dash" keeps its historical
            // override so the dash trail is unaffected by presets that never opt in.
            main.simulationSpace = (p.worldSpace || kind == "dash")
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;

            // ---- Emission ----
            var emission = ps.emission;
            if (isBurst || isBurstLoop)
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)p.count) });
            }
            else
            {
                emission.rateOverTime = Mathf.Max(1f, p.emitRate);
                // A previously applied burst preset leaves its Burst in the list, and the
                // list is independent of rateOverTime — the old burst would keep firing on
                // top of the new continuous emitter at every duration boundary.
                emission.burstCount = 0;
            }

            // ---- Rotation ----
            // Both modules are written unconditionally: a preset that leaves rotation at
            // zero must actively switch it off, or a reused emitter keeps spinning with
            // whatever the previously applied preset asked for.
            float jitter = Mathf.Abs(p.startRotationJitterDegrees) * Mathf.Deg2Rad;
            main.startRotation = jitter > 0f
                ? new ParticleSystem.MinMaxCurve(-jitter, jitter)
                : new ParticleSystem.MinMaxCurve(0f);

            var rot = ps.rotationOverLifetime;
            if (Mathf.Abs(p.rotationSpeedDegrees) > 0.01f)
            {
                float rad = p.rotationSpeedDegrees * Mathf.Deg2Rad;
                rot.enabled = true;
                // Symmetric range so each particle picks its own spin direction — a whole
                // system turning the same way reads as a rotating texture, not as fire.
                rot.z = new ParticleSystem.MinMaxCurve(-rad, rad);
            }
            else
            {
                rot.enabled = false;
            }

            // ---- Shape ----
            ConfigureShape(ps, p, scale);

            // ---- Size Over Lifetime ----
            var sol = ps.sizeOverLifetime;
            if (p.turnoverCycles > 0)
            {
                // Width oscillates, height does not: the quad reads as a flat object rotating
                // about its own long axis. This is what lets foliage look like foliage while
                // falling down a straight vertical — the impression normally comes from
                // lateral drift, which is exactly what a vertical fall cannot have.
                sol.enabled = true;
                sol.separateAxes = true;
                var height = (p.sizeOverLife != null && p.sizeOverLife.Length > 0)
                    ? BuildAnimationCurve(p.sizeOverLife)
                    : AnimationCurve.Constant(0f, 1f, 1f);
                sol.x = new ParticleSystem.MinMaxCurve(1f,
                    BuildTurnoverCurve(p.turnoverCycles, p.turnoverMinWidth, height));
                sol.y = new ParticleSystem.MinMaxCurve(1f, height);
                sol.z = new ParticleSystem.MinMaxCurve(1f, height);
            }
            else if (p.sizeOverLife != null && p.sizeOverLife.Length > 0)
            {
                sol.enabled = true;
                // Emitters are reused across presets: a preset that turned separateAxes on
                // has to be turned off by the next one, or its height curve silently becomes
                // the next preset's width.
                sol.separateAxes = false;
                sol.size = new ParticleSystem.MinMaxCurve(1f, BuildAnimationCurve(p.sizeOverLife));
            }
            else if (isBurst)
            {
                sol.enabled = true;
                // Expand then shrink for impact/explosion feel
                var curve = new AnimationCurve(
                    new Keyframe(0f, 0.3f),
                    new Keyframe(0.3f, 1.0f),
                    new Keyframe(1.0f, 0f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }
            else
            {
                sol.enabled = false;
            }

            // ---- Velocity Damping (drag) ----
            // Fetched unconditionally: emitters are reused across presets (the editor's
            // preview emitter is), so a module one preset turns on has to be turned off
            // by the next one or its drag silently clamps every effect chosen afterwards.
            var vlim = ps.limitVelocityOverLifetime;
            if (p.drag > 0f)
            {
                vlim.enabled = true;
                vlim.separateAxes = false;
                vlim.dampen = Mathf.Clamp01(p.drag);
                vlim.limit = new ParticleSystem.MinMaxCurve(p.speed * scale);
            }
            else
            {
                vlim.enabled = false;
            }

            // ---- Noise (turbulence) ----
            // Authored noise wins; falling_leaf keeps its legacy sway so the 100-odd presets
            // that predate these fields render exactly as before.
            var noise = ps.noise;
            if (p.noiseEnabled && p.noiseStrength > 0f)
            {
                noise.enabled = true;
                noise.frequency = Mathf.Max(0.0001f, p.noiseFrequency);
                noise.damping = true;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(p.noiseScrollSpeed);

                float nStrength = p.noiseStrength * scale;
                float vScale = Mathf.Clamp01(p.noiseVerticalScale);
                if (vScale < 1f)
                {
                    // Noise displaces on every axis. When its magnitude approaches the fall
                    // speed the vertical component wins as often as it loses, and particles
                    // that must always descend — leaves — visibly drift back upward.
                    // separateAxes keeps the horizontal flutter at full width while damping
                    // only Y.
                    noise.separateAxes = true;
                    noise.strengthX = new ParticleSystem.MinMaxCurve(nStrength);
                    noise.strengthY = new ParticleSystem.MinMaxCurve(nStrength * vScale);
                    noise.strengthZ = new ParticleSystem.MinMaxCurve(nStrength);
                }
                else
                {
                    noise.separateAxes = false;
                    noise.strength = new ParticleSystem.MinMaxCurve(nStrength);
                }
            }
            else if (kind == "falling_leaf")
            {
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(p.swayAmp * scale);
                noise.frequency = p.swaySpeed;
                noise.damping = true;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.2f);
            }
            else
            {
                noise.enabled = false;
            }

            // ---- Colour Over Lifetime ----
            var col = ps.colorOverLifetime;
            col.enabled = true;
            if (p.alphaOverLife != null && p.alphaOverLife.Length > 0)
                col.color = BuildGradientFromCurves(p);
            else
                col.color = BuildFadeOutGradient(p);

            // ---- Flipbook ----
            ConfigureFlipbook(ps, p);

            // ---- Renderer ----
            ConfigureRenderer(ps, p);
        }

        /// <summary>
        /// Drives Unity's Texture Sheet Animation from <see cref="ParticleVfxParams.flipbookFrames"/>.
        /// Sprites mode (not Grid): the frames are separate assets packed into a SpriteAtlas,
        /// and a grid sheet would mean one oversized texture instead.
        ///
        /// Always runs, including the empty case — emitters are reused across presets (the F1
        /// preview emitter serves every one of them), so a sheet left over from the previously
        /// selected preset would keep animating over the next preset's texture.
        /// </summary>
        private void ConfigureFlipbook(ParticleSystem ps, ParticleVfxParams p)
        {
            var tsa = ps.textureSheetAnimation;
            var frames = p.flipbookFrames;

            if (frames == null || frames.Length == 0)
            {
                tsa.enabled = false;
                return;
            }

            // RemoveSprite shifts the tail down, so walk backwards.
            for (int i = tsa.spriteCount - 1; i >= 0; i--)
                tsa.RemoveSprite(i);

            int added = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null) continue;
                tsa.AddSprite(frames[i]);
                added++;
            }

            if (added == 0)
            {
                tsa.enabled = false;
                return;
            }

            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Sprites;
            tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            tsa.cycleCount = Mathf.Max(1, p.flipbookCycles);
            tsa.startFrame = p.flipbookRandomStartFrame
                ? new ParticleSystem.MinMaxCurve(0f, Mathf.Max(0f, added - 1))
                : new ParticleSystem.MinMaxCurve(0f);
        }

        /// <summary>
        /// Width multiplier for a particle turning over <paramref name="cycles"/> times across
        /// its life: |cos| of the turn angle, floored at <paramref name="minWidth"/> and
        /// multiplied by the height curve so the two axes stay in proportion.
        ///
        /// Sampled rather than keyed at the extremes because a cosine reconstructed from two
        /// keys per cycle is a triangle wave — the leaf would snap between faces instead of
        /// rolling through them.
        /// </summary>
        private static AnimationCurve BuildTurnoverCurve(int cycles, float minWidth, AnimationCurve height)
        {
            const int SAMPLES_PER_CYCLE = 12;
            int n = Mathf.Clamp(cycles, 1, 8) * SAMPLES_PER_CYCLE;
            var curve = new AnimationCurve();
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float w = Mathf.Abs(Mathf.Cos(Mathf.PI * cycles * t));
                w = Mathf.Lerp(Mathf.Clamp(minWidth, 0.02f, 1f), 1f, w);
                curve.AddKey(t, w * height.Evaluate(t));
            }
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        // Box depth on the camera axis (local Y after BoxRotationFor — see below). Hair-thin
        // on purpose: the camera looks down world Z, so anything placed there is invisible.
        // 0.01 rather than 0: either value is equally invisible along the camera axis, so
        // this is not about visibility. It is the pre-existing depth value carried over from
        // before this fix (the old scale's third component), kept hair-thin rather than zero
        // so the shape stays a real (if degenerate) volume for the Scene-view gizmo — the same
        // non-zero-axis convention every other hard-coded Box in this file follows
        // (falling_leaf / water_flow use 0.1).
        private const float BoxDepth = 0.01f;

        private void ConfigureShape(ParticleSystem ps, ParticleVfxParams p, float scale)
        {
            var shape = ps.shape;
            shape.enabled = true;

            // Normalise to Unity's defaults before the switch. Each branch below sets only
            // the properties its own shape cares about, so on a reused emitter a cone's
            // rotation, a box's scale or an aura's edge-only radiusThickness would survive
            // into the next preset and deform it — a ring emitted flat on its side, an
            // explosion emitting from a shell instead of a volume. A freshly spawned
            // emitter never sees that, which is why it only ever showed up in the editor's
            // preview, where one emitter serves every preset.
            shape.position        = Vector3.zero;
            shape.rotation        = Vector3.zero;
            shape.scale           = Vector3.one;
            shape.radiusThickness = 1f;
            shape.angle           = 25f;

            switch (p.kind ?? "")
            {
                case "aura":
                case "healing_aura":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = p.radius * scale;
                    shape.radiusThickness = 0f;       // emit from edge
                    break;

                case "dash":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.1f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "slash":
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = p.arcRangeDegrees * 0.5f;
                    shape.radius = 0.2f * scale;
                    shape.radiusThickness = 1f;
                    shape.rotation = new Vector3(-90f, 0f, 0f); // face forward
                    break;

                case "explosion":
                case "smoke_burst":
                case "firework":
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.1f * scale;
                    break;

                case "smoke_emitter":
                case "smoke":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    // Use dispersion as radius if available (Python emission spread)
                    shape.radius = p.dispersion > 0f ? p.dispersion * scale : 0.15f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "arcane_flame":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.2f * scale;
                    shape.radiusThickness = 1f;
                    break;

                case "water_fountain":
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 15f;
                    shape.radius = 0.05f * scale;
                    shape.radiusThickness = 1f;
                    shape.rotation = new Vector3(-90f, 0f, 0f); // aim upward (Unity Y-up)
                    break;

                case "falling_leaf":
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(2f * scale, 0.1f, 0.1f);
                    break;

                case "water_flow":
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(3f * scale, 0.1f, 0.1f);
                    break;

                case "portal":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    float portalR = p.outerRadius > 0f ? p.outerRadius : p.radius;
                    shape.radius = portalR * scale;
                    shape.radiusThickness = 0f; // emit from edge
                    break;

                default:
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.15f * scale;
                    break;
            }

            // ── Authored overrides — applied AFTER the switch so they beat the kind ──
            //
            // The kinds hard-code their emission areas (falling_leaf a 2-unit strip,
            // water_flow a 3-unit strip, smoke a circle of `dispersion`) and their
            // directions (water_fountain aims up, everything else radial). These two
            // overrides are the first authorable versions of either.

            bool hasArea = p.spawnWidth > 0f || p.spawnHeight > 0f;
            bool hasDir  = p.directionDegrees >= 0f;

            if (hasArea)
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                // Unity's Box shape spawns from its whole volume and throws along local +Z.
                // With the scale below that volume is a width x height rectangle in local XZ,
                // hair-thin on local Y.
                // AimShapeAt (FromToRotation) only pins that +Z to the heading — the other
                // two axes are whatever the rotation happens to leave them, and for a 90°
                // (up) heading that turned out to be a -90° turn about X, which swaps local
                // Y and Z. That mapped the authored HEIGHT onto local Y, i.e. onto world Z —
                // the camera axis on this orthographic top-down setup — so Area Height spent
                // its extent as invisible depth and the box read as the width alone, a line.
                // BoxRotationFor pins local Y to world Z explicitly instead, so the authored
                // width/height axes always land in the visible screen plane regardless of
                // heading, and only the intentionally hair-thin BoxDepth axis ever falls on Z.
                shape.scale = new Vector3(
                    Mathf.Max(0.01f, p.spawnWidth) * scale,
                    BoxDepth,
                    Mathf.Max(0.01f, p.spawnHeight) * scale);
                shape.radiusThickness = 1f;
                shape.rotation = BoxRotationFor(hasDir ? p.directionDegrees : 90f);
            }
            else if (hasDir)
            {
                // Direction without an area: a cone whose axis is the heading and whose
                // half-angle is the spread. `speed` becomes the throw along it — the same
                // recipe water_fountain hard-codes for "up", generalised to any angle.
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = Mathf.Clamp(p.directionSpreadDegrees, 0f, 89f);
                shape.radius = Mathf.Max(0.02f, p.dispersion) * scale;
                shape.radiusThickness = 1f;
                shape.rotation = AimShapeAt(p.directionDegrees);
            }
        }

        /// <summary>
        /// Euler rotation that points a shape's emission axis (local +Z) along a 2D heading:
        /// 0 = right, 90 = up, counter-clockwise. Derived via FromToRotation rather than
        /// hand-built angles because composing an X-flip with a Z-spin by hand is exactly
        /// how water_fountain's "aim upward (Unity Y-up)" comment came to need a comment.
        /// Only safe for shapes that are symmetric about their emission axis — a Cone is,
        /// so FromToRotation's under-specified roll around +Z never matters there. A Box is
        /// NOT symmetric (it has a width axis and a height axis besides the emission axis),
        /// which is exactly what made this rotation wrong for the box — see BoxRotationFor.
        /// </summary>
        private static Vector3 AimShapeAt(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            return Quaternion.FromToRotation(Vector3.forward, dir).eulerAngles;
        }

        /// <summary>
        /// Euler rotation for the spawn-area Box: local +Z (Unity's box emission/throw axis)
        /// along the 2D heading (0 = right, 90 = up, counter-clockwise), with local +Y pinned
        /// to world +Z. Unlike <see cref="AimShapeAt"/>'s FromToRotation, which leaves the
        /// roll around the aimed axis unspecified and let it fall wherever the shortest
        /// rotation happened to land, this fixes ALL three box axes on purpose: local X
        /// (width) and local Z (height, along the heading) always stay in the world XY
        /// screen plane, and only the intentionally hair-thin depth axis (local Y) is ever
        /// placed on world Z, the camera's look axis. That is what keeps Area Width and
        /// Area Height both visible at every heading instead of one of them landing on Z.
        /// </summary>
        private static Vector3 BoxRotationFor(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            var headingDir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            return Quaternion.LookRotation(headingDir, Vector3.forward).eulerAngles;
        }

    }
}