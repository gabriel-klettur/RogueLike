def create_empty_component_store():
    """
    Devuelve un diccionario con todas las claves de componentes inicializadas a diccionarios vacíos.
    """
    return {
        'Position': {}, 'Sprite': {}, 'MovementSpeed': {},
        'PatrolRoute': {}, 'NPCState': {}, 'Animator': {}, 'AnimationTimer': {},
        'Health': {}, 'Mana': {}, 'Energy': {}, 'Hunger': {}, 'Scale': {}, 'Identity': {}, 'Velocity': {}, 'MultiCollider': {},
        'ZLayer': {}, 'DeathTimer': {}, 'DamageConfig': {}, 'FireballComponent': {}, 'ArcaneFlameComponent': {}, 'FireworkLaunchComponent': {}, 'SmokeComponent': {},
        'SmokeEmitterComponent': {},
        'SphereMagicShieldComponent': {},
        'TeleportComponent': {},
        'ExplosionComponent': {},
        'AuraComponent': {}, 'LaserBeamComponent': {}, 'ParticleComponent': {}, 'SlashEmitterComponent': {},
        'HitboxComponent': {},
        'SpawnRequest': {}, 'CombatStats': {}, 'MeleeWeapon': {}, 'MeleeRange': {},
        'WantsToMelee': {}, 'AttackCooldown': {}, 'WantsToCastSpell': {}, 'AggroRange': {},
        # AI defend/leash area per NPC
        'DefendArea': {},
        'ChaseTarget': {}, 'FacingCooldown': {}, 'InputComponent': {}, 'InventoryComponent': {}, 'PhysicalItemComponent': {}, 'CollectibleComponent': {}, 'ExperienceComponent': {},
        'CameraFollowComponent': {}, 'PlayerTagComponent': {}, 'NPCTagComponent': {}, 'MonsterInstanceComponent': {}, 'InCombat': {},
        'TempZLayer': {},
        'FlashComponent': {}, 'TrailComponent': {}, 'GrayscaleComponent': {},
        # Spawner components
        'SpawnerConfig': {}, 'SpawnerState': {},
        # Buildings
        'BuildingHealth': {},            # key -> { current_hp, max_hp }
    }
# Path: src/roguelike_game/ecs/core/component_registry.py