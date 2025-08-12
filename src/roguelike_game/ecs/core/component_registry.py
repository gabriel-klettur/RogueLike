
def create_empty_component_store():
    """
    Devuelve un diccionario con todas las claves de componentes inicializadas a diccionarios vacíos.
    """
    return {
        'Position': {}, 'Sprite': {}, 'Patrol': {}, 'MovementSpeed': {},
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
        'ChaseTarget': {}, 'FacingCooldown': {}, 'InputComponent': {}, 'InventoryComponent': {}, 'PhysicalItemComponent': {}, 'CollectibleComponent': {}, 'ExperienceComponent': {},
        'CameraFollowComponent': {}, 'PlayerTagComponent': {}, 'NPCTagComponent': {}, 'MonsterInstanceComponent': {}, 'InCombat': {},
        'FlashComponent': {}, 'TrailComponent': {}, 'GrayscaleComponent': {},
    }
# Path: src/roguelike_game/ecs/core/component_registry.py