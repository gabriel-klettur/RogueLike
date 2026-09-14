using System.Collections.Generic;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// The WHOLE loading bar: the scene-load segment in front, then the boot's own etapas.
    /// Pure, so every mapping below is a fixture rather than a screenshot.
    ///
    /// <para><b>The scene used to own a flat 40 % of the bar.</b> A constant, measured by
    /// nobody: on a warm machine the scene loads in a fraction of the boot and the bar raced
    /// to 40 % and then crawled, and on a cold one the opposite. The share is now the
    /// scene's PREDICTED milliseconds over the whole predicted load, and 40 % survives only as
    /// the answer for a machine that has never booted.</para>
    ///
    /// <para><b>The scene share is fixed for the life of one screen.</b> The boot's own plan is
    /// replaced by the live one the moment the sequence is built (a release build has no
    /// editor etapa, a new step has appeared), but moving the scene share at that instant
    /// would move the bar's already-filled part under the player — and the bar may never go
    /// backwards. <see cref="WithBoot"/> keeps it.</para>
    /// </summary>
    public sealed class BootScreenPlan
    {
        /// <summary>What a machine with no measurement gives the scene.</summary>
        public const float FallbackSceneShare = 0.4f;

        /// <summary>Of the scene segment, how much is loading (the rest is activation), unmeasured.</summary>
        public const float FallbackLoadShare = 0.85f;

        private const float MinSceneShare = 0.05f;
        private const float MaxSceneShare = 0.80f;

        public float SceneShare { get; }
        public float LoadShare { get; }
        public float PredictedSceneLoadMs { get; }
        public float PredictedActivationMs { get; }
        public BootPlan Boot { get; }

        public bool IsTimed => Boot.IsTimed && PredictedSceneLoadMs > 0f;
        public float PredictedSceneMs => (PredictedSceneLoadMs > 0f ? PredictedSceneLoadMs : 0f)
                                       + (PredictedActivationMs > 0f ? PredictedActivationMs : 0f);
        public float TotalPredictedMs => PredictedSceneMs + Boot.TotalPredictedMs;

        private BootScreenPlan(float sceneShare, float loadShare, float loadMs, float activationMs, BootPlan boot)
        {
            SceneShare = sceneShare;
            LoadShare = loadShare;
            PredictedSceneLoadMs = loadMs;
            PredictedActivationMs = activationMs;
            Boot = boot ?? BootPlan.Empty;
        }

        public static BootScreenPlan Compose(float sceneLoadMs, float activationMs, BootPlan boot)
        {
            boot = boot ?? BootPlan.Empty;
            float share = FallbackSceneShare;
            float scene = (sceneLoadMs > 0f ? sceneLoadMs : 0f) + (activationMs > 0f ? activationMs : 0f);
            if (boot.IsTimed && sceneLoadMs > 0f && boot.TotalPredictedMs > 0f)
                share = Clamp(scene / (scene + boot.TotalPredictedMs), MinSceneShare, MaxSceneShare);

            float loadShare = FallbackLoadShare;
            if (sceneLoadMs > 0f && activationMs >= 0f && scene > 0f)
                loadShare = Clamp(sceneLoadMs / scene, 0.3f, 0.95f);

            return new BootScreenPlan(share, loadShare, sceneLoadMs, activationMs, boot);
        }

        /// <summary>Same scene share, a new boot plan. See the class note for why.</summary>
        public BootScreenPlan WithBoot(BootPlan boot)
            => new BootScreenPlan(SceneShare, LoadShare, PredictedSceneLoadMs, PredictedActivationMs, boot);

        /// <summary>Scene load progress 0..1 to bar space.</summary>
        public float MapSceneLoad(float p) => Clamp01(p) * SceneShare * LoadShare;

        /// <summary>Activation progress 0..1 to bar space.</summary>
        public float MapActivation(float p) => SceneShare * (LoadShare + (1f - LoadShare) * Clamp01(p));

        /// <summary>Boot fraction 0..1 to bar space.</summary>
        public float MapBoot(float f) => SceneShare + Clamp01(f) * (1f - SceneShare);

        /// <summary>Scene segment plus every boot segment.</summary>
        public int SegmentCount => 1 + Boot.Count;

        public string SegmentName(int i)
        {
            if (i <= 0) return BootPlan.ScenePhase;
            int b = i - 1;
            return b < Boot.Count ? Boot.Segments[b].Name : string.Empty;
        }

        /// <summary>Where each segment STARTS on the bar. Index 0 is always 0.</summary>
        public void SegmentStarts(List<float> into)
        {
            if (into == null) return;
            into.Clear();
            into.Add(0f);
            for (int i = 0; i < Boot.Count; i++) into.Add(MapBoot(Boot.Segments[i].Start));
        }

        public float SegmentEnd(int i)
        {
            if (i <= 0) return SceneShare;
            int b = i - 1;
            return b < Boot.Count ? MapBoot(Boot.Segments[b].End) : 1f;
        }

        public int SegmentIndexAt(float barFraction)
        {
            if (barFraction < SceneShare || Boot.Count == 0) return 0;
            float f = (1f - SceneShare) > 0f ? (barFraction - SceneShare) / (1f - SceneShare) : 1f;
            return 1 + Boot.IndexAt(f);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
