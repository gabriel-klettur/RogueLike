from __future__ import annotations

# Thin facade to keep backward compatibility while using modular previews
# Expose the same classes that were originally defined here

from .particle_previews import (
    DummyCamera as _DummyCamera,
    ParticlePreviewSmoke,
    ParticlePreviewSmokeBurst,
    ParticlePreviewHealingAura,
    ParticlePreviewAura,
    ParticlePreviewDash,
    ParticlePreviewSlash,
    ParticlePreviewLaser,
    ParticlePreviewExplosion,
    ParticlePreviewArcaneFlame,
    ParticlePreviewFirework,
    ParticlePreviewLightning,
    ParticlePreviewTeleport,
    ParticlePreviewWaterFountain,
    ParticlePreviewFallingLeaf,
    ParticlePreviewWaterFlow,
)

# Backwards-compatibility alias
_DummyCamera = _DummyCamera

__all__ = [
    "_DummyCamera",
    "ParticlePreviewSmoke",
    "ParticlePreviewSmokeBurst",
    "ParticlePreviewHealingAura",
    "ParticlePreviewAura",
    "ParticlePreviewDash",
    "ParticlePreviewSlash",
    "ParticlePreviewLaser",
    "ParticlePreviewExplosion",
    "ParticlePreviewArcaneFlame",
    "ParticlePreviewFirework",
    "ParticlePreviewLightning",
    "ParticlePreviewTeleport",
    "ParticlePreviewWaterFountain",
    "ParticlePreviewFallingLeaf",
    "ParticlePreviewWaterFlow",
]
