using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Covers <see cref="ManaRegenSilhouette"/>'s gate (mana-regen state +
    /// source visibility) and its mirroring of the body SpriteRenderer.
    ///
    /// The component's fade tween is driven by <c>Time.deltaTime</c>, which is
    /// effectively zero between reflection-driven LateUpdate calls in EditMode.
    /// We therefore test the steady-state branches by writing the private
    /// <c>_visibility</c> field directly (force-set to 1.0 for the "regen"
    /// branch and to 0.0 for the "settled-after-fade" branch). The fade math
    /// itself is just a Mathf.MoveTowards — not worth a test.
    /// </summary>
    public class ManaRegenSilhouetteTests
    {
        private GameObject _playerGo;
        private Mana _mana;
        private ManaRegenSilhouette _silhouette;
        private SpriteRenderer _sourceSr;
        private Texture2D _texture;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_texture != null)  Object.DestroyImmediate(_texture);
            _playerGo = null;
            _texture = null;
            LogAssert.ignoreFailingMessages = false;
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private void BuildPlayerWithSprite()
        {
            _playerGo = new GameObject("PlayerWithSilhouette");
            _mana = _playerGo.AddComponent<Mana>();
            _mana.Initialize(100, 5f);

            var bodyChild = new GameObject("Body");
            bodyChild.transform.SetParent(_playerGo.transform, false);
            _sourceSr = bodyChild.AddComponent<SpriteRenderer>();
            _texture = new Texture2D(8, 8);
            _sourceSr.sprite = Sprite.Create(_texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 16f);

            _silhouette = _playerGo.AddComponent<ManaRegenSilhouette>();
            InvokeAwake();
        }

        private void InvokeAwake() => InvokeNonPublic("Awake");
        private void InvokeLateUpdate() => InvokeNonPublic("LateUpdate");

        private void InvokeNonPublic(string methodName)
        {
            typeof(ManaRegenSilhouette)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_silhouette, null);
        }

        private SpriteRenderer GetAuraRenderer()
        {
            if (_sourceSr == null) return null;
            var t = _sourceSr.transform.Find("ManaRegenSilhouetteSprite");
            return t != null ? t.GetComponent<SpriteRenderer>() : null;
        }

        private void SetPrivateVisibility(float v)
        {
            typeof(ManaRegenSilhouette)
                .GetField("_visibility", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_silhouette, v);
        }

        private void ForceRegenDelayElapsed()
        {
            typeof(Mana)
                .GetField("_lastConsumeTime", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_mana, Time.time - 10f);
        }

        // ── tests ───────────────────────────────────────────────────────────

        [Test]
        public void Awake_WithoutSpriteRenderer_DoesNotThrowAndBuildsNothing()
        {
            _playerGo = new GameObject("PlayerNoSprite");
            _mana = _playerGo.AddComponent<Mana>();
            _mana.Initialize(100, 5f);
            _silhouette = _playerGo.AddComponent<ManaRegenSilhouette>();

            Assert.DoesNotThrow(InvokeAwake,
                "Silhouette must short-circuit gracefully when no SpriteRenderer is reachable.");
            Assert.IsNull(GetAuraRenderer(),
                "No source → no aura child built.");
        }

        [Test]
        public void Awake_WithSprite_BuildsChildSpriteRendererBehindSource()
        {
            BuildPlayerWithSprite();

            var aura = GetAuraRenderer();
            Assert.IsNotNull(aura, "Aura child SpriteRenderer must exist after Awake.");
            Assert.AreEqual(_sourceSr.sortingOrder - 1, aura.sortingOrder,
                "Aura must sort one rung below the source so only the rim peeks out.");
            Assert.AreSame(_sourceSr.transform, aura.transform.parent,
                "Aura must parent under the source so it inherits position + flips.");
        }

        [Test]
        public void Awake_AuraStartsHidden()
        {
            BuildPlayerWithSprite();

            Assert.IsFalse(GetAuraRenderer().enabled,
                "Aura starts disabled — only LateUpdate may turn it on.");
        }

        [Test]
        public void LateUpdate_WhileRegenerating_EnablesAura()
        {
            BuildPlayerWithSprite();
            _mana.TryConsume(40);
            ForceRegenDelayElapsed();
            SetPrivateVisibility(1f);   // skip the 0.35 s fade ramp for determinism

            InvokeLateUpdate();

            Assert.IsTrue(GetAuraRenderer().enabled,
                "regen + visibility above threshold → aura must render.");
        }

        [Test]
        public void LateUpdate_AtFullManaWithVisibilityZero_KeepsAuraHidden()
        {
            BuildPlayerWithSprite();
            SetPrivateVisibility(0f);   // steady-state after the fade-out has settled

            InvokeLateUpdate();

            Assert.IsFalse(GetAuraRenderer().enabled,
                "Settled at full mana, aura must be hidden — no GPU work.");
        }

        [Test]
        public void LateUpdate_MirrorsSourceFlipXAndSprite()
        {
            BuildPlayerWithSprite();
            _mana.TryConsume(40);
            ForceRegenDelayElapsed();
            SetPrivateVisibility(1f);

            _sourceSr.flipX = true;
            InvokeLateUpdate();

            var aura = GetAuraRenderer();
            Assert.IsTrue(aura.flipX, "Aura must mirror flipX so directional sprites stay aligned.");
            Assert.AreSame(_sourceSr.sprite, aura.sprite,
                "Aura must mirror the current sprite so animation swaps carry over.");
        }

        [Test]
        public void LateUpdate_DisabledWhenSourceDisabled()
        {
            BuildPlayerWithSprite();
            _mana.TryConsume(40);
            ForceRegenDelayElapsed();
            SetPrivateVisibility(1f);

            _sourceSr.enabled = false;
            InvokeLateUpdate();

            Assert.IsFalse(GetAuraRenderer().enabled,
                "Aura must hide whenever the body sprite is hidden (e.g. spirit form).");
        }

        [Test]
        public void DefaultColor_LeansBlue()
        {
            BuildPlayerWithSprite();

            var color = (Color)typeof(ManaRegenSilhouette)
                .GetField("_auraColor", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_silhouette);

            Assert.Greater(color.b, color.r,
                "Mana-regen silhouette must default to a blue tint, not the legacy yellow.");
            Assert.Greater(color.b, color.g,
                "Mana-regen silhouette must default to a blue tint (vs green).");
        }
    }
}
