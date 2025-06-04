# Path: src/roguelike_game/ecs/core/component_registry.py

def create_empty_component_store():
    """
    Devuelve un diccionario con todas las claves de componentes inicializadas a diccionarios vacíos.
    """
    return {
        'Position': {}, 'Sprite': {}, 'Patrol': {}, 'MovementSpeed': {},
        'PatrolRoute': {}, 'NPCState': {}, 'Animator': {}, 'AnimationTimer': {},
        'Health': {}, 'Scale': {}, 'Identity': {}, 'Velocity': {}, 'MultiCollider': {},
        'ZLayer': {}, 'DeathTimer': {}, 'DamageConfig': {}, 'FireballComponent': {},
        'AuraComponent': {}, 'LaserBeamComponent': {}, 'ParticleComponent': {},
        'HitboxComponent': {},
        'SpawnRequest': {}, 'CombatStats': {}, 'MeleeWeapon': {}, 'MeleeRange': {},
        'WantsToMelee': {}, 'AttackCooldown': {}, 'WantsToCastSpell': {}, 'AggroRange': {},
        'ChaseTarget': {}, 'FacingCooldown': {}, 'InputComponent': {}, 'InventoryComponent': {},
        'CameraFollowComponent': {}, 'PlayerTagComponent': {}, 'InCombat': {},
        'FlashComponent': {}, 'TrailComponent': {},
    }
