using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Pins that <see cref="EntityAnimationBinder.ApplyVisuals"/> actually reads
    /// <see cref="AnimationScaleConfig.animationSpeedMultiplier"/> and forwards it to the
    /// bound <see cref="DirectionalAnimator"/>. The field alone doing nothing would be the
    /// same silent-dead-knob failure CLAUDE.md already documents for
    /// <c>scaleWalk/Chase/Cast/Attack/Damage/Death</c>.
    /// </summary>
    public class EntityAnimationBinderAnimationSpeedTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private List<Sprite> CreateFrames(int count)
        {
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        private DirectionalAnimator Bind(EntityAssetConfig config)
        {
            var go = new GameObject("BinderTarget");
            _created.Add(go);
            go.AddComponent<SpriteRenderer>();

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _created.Add(def);
            def.assetConfig = config;

            Assert.IsTrue(EntityAnimationBinder.ApplyMonsterVisuals(go, def));

            return go.GetComponent<DirectionalAnimator>();
        }

        [Test]
        public void UnsetMultiplier_LeavesTheAnimatorAtIdentitySpeed()
        {
            // scaleConfig.animationSpeedMultiplier is 0 by construction here — the exact
            // value every asset saved before this field existed deserializes to. Every
            // shipped monster must keep animating at exactly 0.15s/frame.
            var config = new EntityAssetConfig { idleSheets = CreateFrames(8) };

            var animator = Bind(config);

            Assert.AreEqual(1f, animator.AnimationSpeedMultiplier,
                "an un-authored animationSpeedMultiplier must resolve to identity speed, " +
                "not zero or any other value that would change a shipped monster's timing.");
        }

        [Test]
        public void ExplicitMultiplier_ReachesTheAnimator()
        {
            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames(8),
                scaleConfig = new AnimationScaleConfig { animationSpeedMultiplier = 2.5f },
            };

            var animator = Bind(config);

            Assert.AreEqual(2.5f, animator.AnimationSpeedMultiplier,
                "EntityAnimationBinder.ApplyVisuals must forward " +
                "scaleConfig.animationSpeedMultiplier to DirectionalAnimator — an " +
                "authoring knob with no runtime effect is the exact failure mode already " +
                "documented for the six dead per-state scale fields.");
        }

        [Test]
        public void ExplicitMultiplier_ChangesGetStateLength_WithoutChangingFrameCount()
        {
            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames(8),
                attackSheets = CreateFrames(8 * 4), // 4 frames/direction
                scaleConfig = new AnimationScaleConfig { animationSpeedMultiplier = 4f },
            };

            var animator = Bind(config);
            animator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);

            float lengthAt4x = animator.GetStateLength(DirectionalAnimator.AnimState.Attack);

            // Rebuild an identical config at 1x to get the true baseline length, rather
            // than reflecting into frameInterval directly — this exercises the exact
            // production path (ApplyVisuals -> SetAnimationSpeedMultiplier) end to end.
            var baselineConfig = new EntityAssetConfig
            {
                idleSheets = CreateFrames(8),
                attackSheets = CreateFrames(8 * 4),
            };
            var baselineAnimator = Bind(baselineConfig);
            baselineAnimator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);
            float baselineLength = baselineAnimator.GetStateLength(DirectionalAnimator.AnimState.Attack);

            Assert.AreEqual(baselineLength / 4f, lengthAt4x, 0.0001f,
                "This is the whole point of the field: retime a swing WITHOUT touching " +
                "frame count, so AttackState's hit window (sized off GetStateLength) " +
                "moves in lockstep with the visual instead of the two disagreeing.");
        }
    }
}
