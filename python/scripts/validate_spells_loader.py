from roguelike_game.config.spells_config import SPELLS

samples = [
    'fireball', 'laser_beam', 'slash', 'teleport', 'healing_aura'
]
print('Total spells:', len(SPELLS))
for k in samples:
    cfg = SPELLS.get(k)
    print('\n==', k)
    if not cfg:
        print('  MISSING')
        continue
    print('  timings:', dict(
        prepare=cfg.get('prepare_duration'),
        channel=cfg.get('channel_duration'),
        cooldown=cfg.get('cooldown_duration'),
    ))
    print('  rules:', dict(
        allow_movement=cfg.get('allow_movement'),
        lock_cast_direction=cfg.get('lock_cast_direction'),
        interruptible=cfg.get('interruptible'),
        automatic=cfg.get('automatic'),
        automatic_cast_punish=cfg.get('automatic_cast_punish'),
    ))
    print('  effect:', dict(
        speed=cfg.get('speed'), damage=cfg.get('damage'), range=cfg.get('range'),
        lifetime=cfg.get('lifetime'), lifespan=cfg.get('lifespan'), radius=cfg.get('radius'), duration=cfg.get('duration')
    ))
    print('  vfx:', dict(
        sprite=cfg.get('sprite'), scale=cfg.get('scale'), vfx_type=type(cfg.get('vfx')).__name__
    ))
    print('  particles:', dict(
        count=cfg.get('particle_count'), dispersion=cfg.get('particle_dispersion'), colors_type=type(cfg.get('particle_colors')).__name__,
        particle_lifespan=cfg.get('particle_lifespan'), particle_speed=cfg.get('particle_speed')
    ))
