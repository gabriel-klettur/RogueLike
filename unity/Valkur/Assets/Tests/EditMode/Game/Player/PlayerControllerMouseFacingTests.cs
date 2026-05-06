using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    public class PlayerControllerMouseFacingTests
    {
        private readonly List<Object> _createdObjects = new List<Object>();

        private GameObject _cameraGo;
        private Camera _camera;
        private GameObject _playerGo;
        private PlayerController _controller;
        private DirectionalAnimator _animator;
        private SpriteRenderer _renderer;
        private DirectionalAnimator.DirectionalSpriteSet _idleSet;
        private DirectionalAnimator.DirectionalSpriteSet _walkSet;
        private Sprite _idleNorthFrame;
        private Sprite _walkWestFrame;
        private Sprite _walkNorthFrame;

        [SetUp]
        public void SetUp()
        {
            if (Mouse.current == null)
                InputSystem.AddDevice<Mouse>();

            _cameraGo = new GameObject("Main Camera");
            _createdObjects.Add(_cameraGo);
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.tag = "MainCamera";
            _camera.orthographic = true;
            _camera.orthographicSize = 10f;
            _camera.transform.position = new Vector3(0f, 0f, -10f);

            _playerGo = new GameObject("Player");
            _createdObjects.Add(_playerGo);
            _playerGo.transform.position = Vector3.zero;
            _playerGo.AddComponent<Rigidbody2D>();
            var health = _playerGo.AddComponent<Health>();
            health.Initialize(100);

            var spriteGo = new GameObject("Sprite");
            _createdObjects.Add(spriteGo);
            spriteGo.transform.SetParent(_playerGo.transform, false);
            _renderer = spriteGo.AddComponent<SpriteRenderer>();

            _animator = _playerGo.AddComponent<DirectionalAnimator>();
            SetAnimatorRenderer(_renderer);
            _idleSet = CreateUniqueSet("idle", 3);
            _walkSet = CreateUniqueSet("walk", 3);
            _idleNorthFrame = _idleSet.north[0];
            _walkWestFrame = _walkSet.west[1];
            _walkNorthFrame = _walkSet.north[1];
            _animator.SetSpriteSets(_idleSet, _walkSet, _walkSet, _walkSet, _walkSet, _idleSet, _idleSet);
            _renderer.sprite = _idleSet.south[0];

            _controller = _playerGo.AddComponent<PlayerController>();
            SetPrivateField("_animator", _animator);
            SetPrivateField("spriteRenderer", _renderer);
            SetPrivateField("_mainCamera", _camera);
            SetPrivateField("_health", health);
        }

        [TearDown]
        public void TearDown()
        {
            // Always release the test-mouse override so subsequent fixtures
            // (and especially MouseInputManagerLegacyFallbackTests, which
            // exercise the production fallback) don't pick up leaked state.
            Valkur.Core.Input.MouseInputManager.SetTestMousePosition(null);

            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                    Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        [Test]
        public void IdlePlayer_UpdateFacesMouseAndUsesIdleSpriteDirection()
        {
            SetMoveInput(Vector2.zero);
            MoveMouseToWorld(new Vector2(0f, 5f));

            InvokeUpdate();

            Assert.Greater(_controller.FacingDirection.y, 0.99f);
            Assert.AreEqual(DirectionalAnimator.AnimState.Idle, _animator.CurrentState);
            Assert.AreEqual(DirectionalAnimator.Direction.North, _animator.CurrentDirection);
            Assert.AreSame(_idleNorthFrame, _renderer.sprite);
        }

        [Test]
        public void MovingPlayer_MouseLeft_UsesMouseDirectionForWalkSprite()
        {
            SetMoveInput(Vector2.right);
            MoveMouseToWorld(new Vector2(-5f, 0f));

            InvokeUpdateFacingDirection();

            Assert.Less(_controller.FacingDirection.x, -0.99f);
            Assert.AreEqual(DirectionalAnimator.AnimState.Walk, _animator.CurrentState);
            Assert.AreEqual(DirectionalAnimator.Direction.West, _animator.CurrentDirection);
            Assert.AreSame(_walkWestFrame, _renderer.sprite);
        }

        [Test]
        public void IdlePlayer_UsesVisualCenterInsteadOfFeetPivotForMouseDirection()
        {
            _renderer.transform.localPosition = new Vector3(0f, 1f, 0f);
            _renderer.sprite = _idleNorthFrame;
            SetMoveInput(Vector2.zero);
            MoveMouseToWorld(new Vector2(1f, 1f));

            InvokeUpdateFacingDirection();

            Assert.Greater(_controller.FacingDirection.x, 0.99f);
            Assert.AreEqual(0f, _controller.FacingDirection.y, 0.001f);
            Assert.AreEqual(DirectionalAnimator.Direction.East, _animator.CurrentDirection,
                "Facing must be computed from the visible character center, not the feet/pivot.");
        }

        [TestCase(0f, -5f, DirectionalAnimator.Direction.South)]
        [TestCase(4f, -4f, DirectionalAnimator.Direction.SouthEast)]
        [TestCase(5f, 0f, DirectionalAnimator.Direction.East)]
        [TestCase(4f, 4f, DirectionalAnimator.Direction.NorthEast)]
        [TestCase(0f, 5f, DirectionalAnimator.Direction.North)]
        [TestCase(-4f, 4f, DirectionalAnimator.Direction.NorthWest)]
        [TestCase(-5f, 0f, DirectionalAnimator.Direction.West)]
        [TestCase(-4f, -4f, DirectionalAnimator.Direction.SouthWest)]
        public void IdlePlayer_MouseAroundPlayer_UsesMatchingIdleDirectionAndSprite(
            float mouseX,
            float mouseY,
            DirectionalAnimator.Direction expectedDirection)
        {
            SetMoveInput(Vector2.zero);
            MoveMouseToWorld(new Vector2(mouseX, mouseY));

            InvokeUpdateFacingDirection();

            Assert.AreEqual(expectedDirection, _animator.CurrentDirection);
            Assert.AreSame(GetFirstFrame(_idleSet, expectedDirection), _renderer.sprite,
                "The rendered sprite frame must match the direction resolved from the mouse.");
        }

        [Test]
        public void MovingPlayer_MouseOutsideViewport_FallsBackToMovementWalkSprite()
        {
            SetMoveInput(Vector2.up);
            MoveMouseToScreen(new Vector2(-10f, Screen.height * 0.5f));

            InvokeUpdateFacingDirection();

            Assert.Greater(_controller.FacingDirection.y, 0.99f);
            Assert.AreEqual(DirectionalAnimator.AnimState.Walk, _animator.CurrentState);
            Assert.AreEqual(DirectionalAnimator.Direction.North, _animator.CurrentDirection);
            Assert.AreSame(_walkNorthFrame, _renderer.sprite);
        }

        private void SetMoveInput(Vector2 value)
        {
            SetPrivateField("_moveInput", value);
        }

        private void SetPrivateField(string fieldName, object value)
        {
            var field = typeof(PlayerController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_controller, value);
        }

        private void SetAnimatorRenderer(SpriteRenderer renderer)
        {
            var field = typeof(DirectionalAnimator).GetField("targetRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_animator, renderer);
        }

        private void InvokeUpdateFacingDirection()
        {
            var method = typeof(PlayerController).GetMethod(
                "UpdateFacingDirection",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(_controller, null);
        }

        private void InvokeUpdate()
        {
            var method = typeof(PlayerController).GetMethod(
                "Update",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(_controller, null);
        }

        private void MoveMouseToWorld(Vector2 worldPosition)
        {
            MoveMouseToScreen((Vector2)_camera.WorldToScreenPoint(worldPosition));
        }

        private static void MoveMouseToScreen(Vector2 screenPosition)
        {
            // Drive both the InputSystem synthetic device (so any callers
            // reading Mouse.current see the new position) AND the
            // MouseInputManager test override (so PlayerController et al.
            // get a deterministic value without depending on the Editor's
            // focus/viewport state, which is what made these tests flaky).
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = screenPosition });
            InputSystem.Update();
            Valkur.Core.Input.MouseInputManager.SetTestMousePosition(screenPosition);
        }

        private DirectionalAnimator.DirectionalSpriteSet CreateUniqueSet(string prefix, int framesPerDirection)
        {
            return new DirectionalAnimator.DirectionalSpriteSet
            {
                south = CreateFrames($"{prefix}_south", framesPerDirection),
                southEast = CreateFrames($"{prefix}_southEast", framesPerDirection),
                east = CreateFrames($"{prefix}_east", framesPerDirection),
                northEast = CreateFrames($"{prefix}_northEast", framesPerDirection),
                north = CreateFrames($"{prefix}_north", framesPerDirection),
                northWest = CreateFrames($"{prefix}_northWest", framesPerDirection),
                west = CreateFrames($"{prefix}_west", framesPerDirection),
                southWest = CreateFrames($"{prefix}_southWest", framesPerDirection)
            };
        }

        private Sprite[] CreateFrames(string name, int count)
        {
            var texture = new Texture2D(count, 1);
            texture.name = name + "_texture";
            _createdObjects.Add(texture);

            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames[i].name = $"{name}_{i}";
                _createdObjects.Add(frames[i]);
            }

            return frames;
        }

        private static Sprite GetFirstFrame(
            DirectionalAnimator.DirectionalSpriteSet set,
            DirectionalAnimator.Direction direction)
        {
            var frames = set.GetFrames(direction);
            Assert.IsNotNull(frames);
            Assert.Greater(frames.Length, 0);
            return frames[0];
        }
    }
}
