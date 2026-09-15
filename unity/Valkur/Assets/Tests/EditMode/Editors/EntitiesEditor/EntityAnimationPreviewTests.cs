using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Entities;

namespace Valkur.Tests.EditMode.Editors.EntitiesEditor
{
    /// <summary>
    /// <see cref="EntityAnimationPreviewService"/> — the off-screen stage behind the Entities
    /// editor's Animation panel.
    ///
    /// What these tests can and cannot see, stated rather than implied:
    ///   • They CAN assert the stage's structure — layer, camera, rig count, what the bind
    ///     resolved — because all of it is set synchronously by the calls under test.
    ///   • They CANNOT assert anything the animator does over TIME. Unity never calls Awake or
    ///     Update on a component added in Edit Mode, so no frame ever advances here; that the
    ///     bind seeds `renderer.sprite` is what makes a still frame visible at all.
    /// </summary>
    [TestFixture]
    public class EntityAnimationPreviewTests
    {
        private readonly List<Object> _created = new List<Object>();
        private EntityAnimationPreviewService _service;
        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _host = new GameObject("AnimPreviewHost");
            _created.Add(_host);
            _service = new EntityAnimationPreviewService();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Shutdown();
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                // Destroy is an ERROR in Edit Mode, not a warning.
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Eight frames — one per direction bucket. `CreateSetFromLinearFrames` cuts a linear
        /// list into eight CONTIGUOUS buckets, so eight sprites is the smallest list that
        /// fills all of them with one frame each.
        /// </summary>
        private List<Sprite> EightFrames(string prefix)
        {
            var frames = new List<Sprite>(8);
            for (int i = 0; i < 8; i++)
            {
                var tex = new Texture2D(4, 4);
                _created.Add(tex);
                var sprite = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f), 16f);
                sprite.name = $"{prefix}_{i}";
                _created.Add(sprite);
                frames.Add(sprite);
            }
            return frames;
        }

        /// <summary>An arbitrary frame count, for a state that really animates.</summary>
        private List<Sprite> ManyFrames(string prefix, int count)
        {
            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(4, 4);
                _created.Add(tex);
                var sprite = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f), 16f);
                sprite.name = $"{prefix}_{i}";
                _created.Add(sprite);
                frames.Add(sprite);
            }
            return frames;
        }

        private EntityAssetConfig ConfigWithIdle() => new EntityAssetConfig
        {
            idleSheets = EightFrames("idle"),
            directionLayout = EntitySheetDirectionLayout.EightDirectional
        };

        // ── Stage ────────────────────────────────────────────────────────────────

        [Test]
        public void Initialize_PutsTheCameraOnThePreviewLayer_AndLeavesItOff()
        {
            _service.Initialize(_host.transform);

            var camera = _host.GetComponentInChildren<Camera>(includeInactive: true);
            Assert.That(camera, Is.Not.Null, "the stage must build its own camera");
            int layer = LayerMask.NameToLayer("SpellPreview");
            Assert.That(layer, Is.GreaterThanOrEqualTo(0),
                "the SpellPreview layer must exist in TagManager — the preview reuses it " +
                "because all 32 physics layers are spent");
            Assert.That(camera.cullingMask, Is.EqualTo(1 << layer),
                "the preview camera must see ONLY the preview layer, or the stage renders the world");
            Assert.That(camera.enabled, Is.False,
                "a camera enabled before Open renders every frame the panel is shut");
            Assert.That(camera.orthographic, Is.True);
        }

        [Test]
        public void Open_EnablesTheCamera_AndCloseTurnsItOffAgain()
        {
            _service.Initialize(_host.transform);
            var camera = _host.GetComponentInChildren<Camera>(includeInactive: true);

            _service.Open();
            Assert.That(camera.enabled, Is.True);
            Assert.That(_service.IsOpen, Is.True);

            _service.Close();
            Assert.That(camera.enabled, Is.False);
            Assert.That(_service.IsOpen, Is.False);
        }

        [Test]
        public void Shutdown_ReleasesTheTexture()
        {
            _service.Initialize(_host.transform);
            Assert.That(_service.GetPreviewTexture(), Is.Not.Null);

            _service.Shutdown();
            Assert.That(_service.GetPreviewTexture(), Is.Null,
                "a released RenderTexture must not be handed to a RawImage");
        }

        // ── Subject ──────────────────────────────────────────────────────────────

        [Test]
        public void SetSubject_BindsThroughTheGameBinder()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");

            Assert.That(_service.HasSubject, Is.True);
            Assert.That(_service.RigCount, Is.EqualTo(1), "one body unless the grid is on");

            var animator = _service.Animator;
            Assert.That(animator, Is.Not.Null,
                "the rig must carry a DirectionalAnimator — it is the binder that adds it, " +
                "which is what makes this preview show what the game shows");
            Assert.That(animator.IdleSprites.south, Is.Not.Null.And.Length.EqualTo(1));

            var renderer = animator.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.Not.Null,
                "the bind seeds the first frame; without it nothing renders in Edit Mode, " +
                "where no Update ever runs");
            Assert.That(renderer.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("SpellPreview")));
        }

        [Test]
        public void ReStagingTheSameEntity_KeepsTheVariantAndTheLoadout()
        {
            var config = ConfigWithIdle();
            config.attackVariants = new List<AttackVariant>
            {
                new AttackVariant { key = "punch", sheets = EightFrames("punch") },
            };

            _service.Initialize(_host.transform);
            _service.SetSubject(config, "dummy");
            _service.SetState(DirectionalAnimator.AnimState.Attack);
            _service.SetVariant(0);

            // Every pacing edit re-binds the rig through this same call so the change shows on
            // the animation being watched. Resetting here made the FIRST edit drop the
            // selection, and the next one then wrote nothing — measured on the Hold toggle,
            // which refuses when no variant is selected.
            _service.SetSubject(config, "dummy");

            Assert.That(_service.CurrentVariant, Is.EqualTo(0));
        }

        [Test]
        public void StagingADifferentEntity_StillDropsTheVariant()
        {
            var first = ConfigWithIdle();
            first.attackVariants = new List<AttackVariant>
            {
                new AttackVariant { key = "punch", sheets = EightFrames("punch") },
            };

            _service.Initialize(_host.transform);
            _service.SetSubject(first, "first");
            _service.SetState(DirectionalAnimator.AnimState.Attack);
            _service.SetVariant(0);

            _service.SetSubject(ConfigWithIdle(), "second");

            Assert.That(_service.CurrentVariant, Is.EqualTo(-1),
                "a variant index belongs to ONE entity's table and means something else in the " +
                "next one's");
        }

        [Test]
        public void SetSubject_WithNoArt_LeavesTheStageEmptyRatherThanShowingThePrevious()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "first");
            Assert.That(_service.HasSubject, Is.True);

            // An idle that resolves to no frames is what EntityAnimationBinder refuses outright.
            _service.SetSubject(new EntityAssetConfig(), "artless");

            Assert.That(_service.RigCount, Is.EqualTo(0));
            Assert.That(_service.HasSubject, Is.False,
                "showing the previous entity's body under a new entity's name is worse than " +
                "showing nothing");
        }

        [Test]
        public void ShowAllDirections_BuildsTheEightBodiesOfTheGrid()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");

            _service.SetShowAllDirections(true);
            Assert.That(_service.RigCount, Is.EqualTo(8),
                "the 3x3 pad has eight facings and a centre that is not one");

            _service.SetShowAllDirections(false);
            Assert.That(_service.RigCount, Is.EqualTo(1));
        }

        [Test]
        public void PickingADirection_LeavesTheGridView()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");
            _service.SetShowAllDirections(true);

            _service.SetDirection(DirectionalAnimator.Direction.East);

            Assert.That(_service.ShowAllDirections, Is.False);
            Assert.That(_service.CurrentDirection, Is.EqualTo(DirectionalAnimator.Direction.East));
            Assert.That(_service.RigCount, Is.EqualTo(1));
        }

        // ── Selection ────────────────────────────────────────────────────────────

        [Test]
        public void ChangingState_DropsTheVariantSelection()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");

            _service.SetVariant(1);
            Assert.That(_service.CurrentVariant, Is.EqualTo(1));

            _service.SetState(DirectionalAnimator.AnimState.Attack);

            Assert.That(_service.CurrentVariant, Is.EqualTo(-1),
                "a variant index belongs to ONE state — attack #1 and cast #1 are unrelated " +
                "animations, so carrying the number across selects something nobody chose");
            Assert.That(_service.CurrentState, Is.EqualTo(DirectionalAnimator.AnimState.Attack));
        }

        [Test]
        public void TheStageSurvivesBeingPointedAtNothing()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(null, null);

            Assert.That(_service.HasSubject, Is.False);
            Assert.That(_service.RigCount, Is.EqualTo(0));
            Assert.DoesNotThrow(() => _service.Tick());
            Assert.DoesNotThrow(() => _service.SetState(DirectionalAnimator.AnimState.Death));
        }

        // ── Timing readout ───────────────────────────────────────────────────────

        [Test]
        public void TheThreeMultipliers_AreReportedSeparately()
        {
            var config = ConfigWithIdle();
            config.scaleConfig.animationSpeedMultiplier = 2f;
            config.SetStateSpeedMultiplier("idle", 0.5f);

            _service.Initialize(_host.transform);
            _service.SetSubject(config, "dummy");

            Assert.That(_service.EntitySpeedMultiplier, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(_service.StateSpeedMultiplier, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(_service.VariantSpeedMultiplier, Is.EqualTo(1f).Within(0.0001f),
                "the base set is paced by the entity and the state only");

            // 0.15 / 2 (entity) / 0.5 (state) = 0.15
            Assert.That(_service.FrameSeconds, Is.EqualTo(0.15f).Within(0.001f),
                "the product is what the clock uses; the three are shown apart so a designer " +
                "can see WHICH dial is doing the work");
        }

        [Test]
        public void ThePlaybackRule_IsStated_BecauseTheNumbersCannotImplyIt()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");

            // One frame per direction here, which is a pose rather than a cycle.
            Assert.That(_service.DescribePlaybackRule(), Does.Contain("single frame"));

            var walking = new EntityAssetConfig
            {
                idleSheets = EightFrames("idle"),
                walkSheets = ManyFrames("walk", 24),
                directionLayout = EntitySheetDirectionLayout.EightDirectional
            };
            _service.SetSubject(walking, "walker");
            _service.SetState(DirectionalAnimator.AnimState.Walk);

            Assert.That(_service.DescribePlaybackRule(), Does.Contain("skips frame 0"),
                "a walk of n frames does NOT take n intervals, and nothing else says so");
        }

        [Test]
        public void AStateWithNoArt_SaysWhichPoseIsOnScreenInstead()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");

            _service.SetState(DirectionalAnimator.AnimState.Walk);
            Assert.That(_service.DescribeFallback(), Does.Contain("idle"),
                "the binder falls walk back to idle, so the stage is showing a pose the state " +
                "does not own — which is indistinguishable from art that exists");

            _service.SetState(DirectionalAnimator.AnimState.Chase);
            Assert.That(_service.DescribeFallback(), Does.Contain("idle"));
        }

        [Test]
        public void AStateWithItsOwnArt_ReportsNoFallback()
        {
            var config = new EntityAssetConfig
            {
                idleSheets = EightFrames("idle"),
                walkSheets = EightFrames("walk"),
                directionLayout = EntitySheetDirectionLayout.EightDirectional
            };

            _service.Initialize(_host.transform);
            _service.SetSubject(config, "dummy");
            _service.SetState(DirectionalAnimator.AnimState.Walk);

            Assert.That(_service.DescribeFallback(), Is.Empty);
        }

        // ── Variant identity ─────────────────────────────────────────────────────

        [Test]
        public void AVariantIsNamedByItsAuthoredKey_EvenWhenAnEarlierOneWasDropped()
        {
            var config = ConfigWithIdle();
            config.attackVariants = new List<AttackVariant>
            {
                new AttackVariant { key = "punch", sheets = EightFrames("punch") },
                new AttackVariant { key = "ghost" },                                  // no frames
                new AttackVariant { key = "kick",  sheets = EightFrames("kick")  },
            };

            _service.Initialize(_host.transform);
            _service.SetSubject(config, "dummy");
            _service.SetState(DirectionalAnimator.AnimState.Attack);

            Assert.That(_service.VariantCount(DirectionalAnimator.AnimState.Attack), Is.EqualTo(2),
                "the binder drops a variant that resolved to no frames");

            // THE reason the editor writes pacing by key and never by index: the rig's index 1
            // is the asset's index 2 here, so a write by position would retune 'ghost'.
            Assert.That(_service.DescribeVariant(DirectionalAnimator.AnimState.Attack, 0),
                Does.StartWith("punch"));
            Assert.That(_service.DescribeVariant(DirectionalAnimator.AnimState.Attack, 1),
                Does.StartWith("kick"),
                "index 1 on the rig is index 2 in the asset — the dropped variant shifts them");
        }

        [Test]
        public void AReservedVariant_SaysWhichSpellsClaimIt()
        {
            var config = ConfigWithIdle();
            config.castVariants = new List<CastVariant>
            {
                new CastVariant
                {
                    key = "spell_3",
                    sheets = EightFrames("cast"),
                    spellKeys = new List<string> { "fireball" }
                },
            };

            _service.Initialize(_host.transform);
            _service.SetSubject(config, "dummy");
            _service.SetState(DirectionalAnimator.AnimState.Cast);

            Assert.That(_service.DescribeVariant(DirectionalAnimator.AnimState.Cast, 0),
                Does.Contain("fireball"),
                "a reservation leaves the rotation, so the panel has to say which spell owns it");
        }

        // ── Transport ────────────────────────────────────────────────────────────

        [Test]
        public void SteppingPausesFirst_OrTheClockEatsTheStep()
        {
            _service.Initialize(_host.transform);
            _service.SetSubject(ConfigWithIdle(), "dummy");
            Assert.That(_service.IsPaused, Is.False);

            _service.StepFrame(1);

            Assert.That(_service.IsPaused, Is.True,
                "a \"next frame\" button that left the clock running shows the frame it asked " +
                "for and loses it on the next tick, which reads as a button that does nothing");
            Assert.That(_service.Animator.Paused, Is.True, "the pause has to reach the rig");
        }

        [Test]
        public void Zoom_IsClamped()
        {
            _service.Initialize(_host.transform);

            for (int i = 0; i < 40; i++) _service.ZoomIn();
            Assert.That(_service.CurrentZoom, Is.LessThanOrEqualTo(6f));

            for (int i = 0; i < 80; i++) _service.ZoomOut();
            Assert.That(_service.CurrentZoom, Is.GreaterThanOrEqualTo(0.25f));
        }
    }
}
