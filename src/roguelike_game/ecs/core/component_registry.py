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
        'AuraComponent': {}, 'LaserBeamComponent': {}, 'ParticleComponent': {}, 'ParticlePresetComponent': {}, 'SlashEmitterComponent': {},
        'HitboxComponent': {},
        'LastAttacker': {},
        'SpawnRequest': {}, 'CombatStats': {}, 'MeleeWeapon': {}, 'MeleeRange': {},
        'WantsToMelee': {}, 'AttackCooldown': {}, 'WantsToCastSpell': {}, 'AggroRange': {},
        'AutoCastComponent': {},
        # AI defend/leash area per NPC
        'DefendArea': {},
        'ChaseTarget': {}, 'FacingCooldown': {}, 'InputComponent': {}, 'InventoryComponent': {}, 'PhysicalItemComponent': {}, 'CollectibleComponent': {}, 'ExperienceComponent': {},
        'CameraFollowComponent': {}, 'PlayerTagComponent': {}, 'NPCTagComponent': {}, 'MonsterInstanceComponent': {}, 'InCombat': {},
        'MonsterArchetype': {},
        # Chat & Vendor
        'ChatComponent': {}, 'VendorComponent': {},
        'TempZLayer': {},
        'FlashComponent': {}, 'TrailComponent': {}, 'RibbonComponent': {}, 'GrayscaleComponent': {},
        # Abilities / Resources
        'DashMeterComponent': {},
        # Combo system
        'ComboCounterComponent': {}, 'ComboRulesComponent': {},
        # Spawner components
        'SpawnerConfig': {}, 'SpawnerState': {}, 'SpawnerChild': {},
        # Buildings
        'BuildingHealth': {},            # key -> { current_hp, max_hp }
        # Lighting
        'LightComponent': {},
    }
# Path: src/roguelike_game/ecs/core/component_registry.py