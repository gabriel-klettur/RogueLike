import logging
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.config.spells_config import SPELLS

logger = logging.getLogger(__name__)

class BoomerangGlowEmitterSystem:
    """
    Crea y mantiene una esfera de partículas anclada a cada boomerang activo.
    - Máximo 1 esfera por boomerang.
    - Limpia la esfera cuando el boomerang desaparece.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'BoomerangGlowEmitterSystem.update')
    def update(self, world, camera=None):
        comps = world.components
        booms = comps.get('BoomerangComponent', {})
        positions = comps.get('Position', {})
        glow_tags = comps.get('BoomerangGlowTag', {})

        # Index existing glow particles by owner_eid
        owner_to_particle = {}
        for peid, tag in list(glow_tags.items()):
            try:
                owner_to_particle[int(getattr(tag, 'owner_eid', -1))] = peid
            except Exception:
                continue

        # Ensure each boomerang has a glow particle
        for eid, bcmp in list(booms.items()):
            if eid in owner_to_particle:
                continue
            pos = positions.get(eid)
            if pos is None:
                continue
            # Visual size from hit_radius or default
            try:
                base_size = int(max(6, min(48, float(getattr(bcmp, 'hit_radius', 12.0)) * 1.2)))
            except Exception:
                base_size = 12
            color = (245, 225, 120)
            lifespan = 9999  # persist; se limpia manualmente al morir el boomerang
            peid = world.create_entity()
            comps.setdefault('Position', {})[peid] = Position(pos.x, pos.y)
            # Anclado al boomerang para seguir su movimiento
            comps.setdefault('ParticleComponent', {})[peid] = ParticleComponent(
                0.0, 0.0, color, base_size, lifespan,
                anchor_eid=eid,
                blend_mode='additive',
                alpha_over_life=[(0.0, 0.95), (1.0, 0.95)],
                size_over_life=[(0.0, 1.0), (1.0, 1.0)],
            )
            # Tag en la partícula, referenciando al boomerang
            try:
                from roguelike_game.ecs.components.particles.boomerang_glow_tag import BoomerangGlowTag
                comps.setdefault('BoomerangGlowTag', {})[peid] = BoomerangGlowTag(owner_eid=eid, particle_eid=peid)
            except Exception:
                # Fallback mínimo si import fail: estructura equivalente
                class _T:
                    def __init__(self, owner_eid, particle_eid):
                        self.owner_eid = owner_eid
                        self.particle_eid = particle_eid
                comps.setdefault('BoomerangGlowTag', {})[peid] = _T(eid, peid)

        # Cleanup: eliminar partículas cuyos boomerangs ya no existen
        alive_boom_ids = set(booms.keys())
        for peid, tag in list(comps.get('BoomerangGlowTag', {}).items()):
            owner = int(getattr(tag, 'owner_eid', -1))
            if owner not in alive_boom_ids:
                try:
                    world.remove_entity(peid)
                except Exception:
                    pass
                try:
                    comps['BoomerangGlowTag'].pop(peid, None)
                except Exception:
                    pass
