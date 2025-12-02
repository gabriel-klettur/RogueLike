import roguelike_engine.config.config as config
import os
import json
import logging

# Importamos cada clase de sistema
from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem
from roguelike_game.ecs.systems.rendering.animation_system import AnimationSystem
from roguelike_game.ecs.systems.rendering.health_bar_system import HealthBarSystem
from roguelike_game.ecs.systems.rendering.mana_bar_render_system import ManaBarRenderSystem
from roguelike_game.ecs.systems.rendering.mana_regen_aura_render_system import ManaRegenAuraRenderSystem
from roguelike_game.ecs.systems.rendering.godmode_aura_render_system import GodmodeAuraRenderSystem
from roguelike_game.ecs.systems.rendering.spawner.spawner_aura_render_system import SpawnerAuraRenderSystem
from roguelike_game.ecs.systems.rendering.nameplate_system import NamePlateSystem
from roguelike_game.ecs.systems.rendering.hud_stats_render_system import HUDStatsRenderSystem
from roguelike_game.ecs.systems.combat.melee.melee_combat_system import MeleeCombatSystem
from roguelike_game.ecs.systems.physics.facing_system import FacingSystem
from roguelike_game.ecs.systems.physics.player_facing_system import PlayerFacingSystem
from roguelike_game.ecs.systems.core.spawn_system import SpawnSystem
from roguelike_game.ecs.systems.core.timed_despawn_system import TimedDespawnSystem
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.systems.combat.spells.spell_casting_system import SpellCastingSystem
from roguelike_game.ecs.systems.ai.auto_cast_system import AutoCastSystem
from roguelike_game.ecs.systems.combat.spells.fireball_system.fireball_system import FireballSystem
from roguelike_game.ecs.systems.combat.spells.arcane_flame_system import ArcaneFlameSystem
from roguelike_game.ecs.systems.combat.spells.firework_launch_system import FireworkLaunchSystem
from roguelike_game.ecs.systems.combat.spells.boomerang_system import BoomerangSystem
from roguelike_game.ecs.systems.combat.spells.aura_system import AuraSystem
from roguelike_game.ecs.systems.particles.healing_aura_emitter_system import HealingAuraEmitterSystem
from roguelike_game.ecs.systems.particles.particle_system import ParticleSystem
from roguelike_game.ecs.systems.rendering.particles.particle_render_system import ParticleRenderSystem
from roguelike_game.ecs.systems.rendering.particles.particle_preset_render_system import ParticlePresetRenderSystem
from roguelike_game.ecs.systems.particles.laser_beam_emitter_system import LaserBeamEmitterSystem
from roguelike_game.ecs.systems.particles.fireball_trail_emitter_system import FireballTrailEmitterSystem
from roguelike_game.ecs.systems.particles.boomerang_glow_emitter_system import BoomerangGlowEmitterSystem
# from roguelike_game.ecs.systems.particles.arcane_flame_emitter_system import ArcaneFlameEmitterSystem
from roguelike_game.ecs.systems.particles.slash_emitter_system import SlashEmitterSystem
from roguelike_game.ecs.systems.particles.dash_emitter_system import DashEmitterSystem
from roguelike_game.ecs.systems.particles.lightning_emitter_system import LightningEmitterSystem
from roguelike_game.ecs.systems.particles.vortex_field_emitter_system import VortexFieldEmitterSystem
from roguelike_game.ecs.systems.lighting.lighting_sync_system import LightingSyncSystem
from roguelike_game.ecs.systems.lighting.torch_light_system import TorchLightSystem
from roguelike_game.ecs.systems.rendering.combat.spells.fireball_render_system import FireballRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.lightning_render_system import LightningRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame_render_system import ArcaneFlameRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch_render_system import FireworkLaunchRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.boomerang_render_system import BoomerangRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.cone_breath_render_system import ConeBreathRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.meteor_shower_render_system import MeteorShowerRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.meteor_fall_render_system import MeteorFallRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.totem_render_system import TotemRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.wall_render_system import WallRenderSystem
from roguelike_game.ecs.systems.combat.spells.smoke_system import SmokeSystem
from roguelike_game.ecs.systems.combat.spells.smoke_emitter_system import SmokeEmitterSystem
from roguelike_game.ecs.systems.combat.spells.teleport_system import TeleportSystem
from roguelike_game.ecs.systems.combat.explosion_system import ExplosionSystem
from roguelike_game.ecs.systems.combat.spells.sphere_magic_shield_system import SphereMagicShieldSystem
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_render_system import SmokeRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter_render_system import SmokeEmitterRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.teleport_render_system import TeleportRenderSystem
from roguelike_game.ecs.systems.rendering.combat.explosions.explosion_render_system import ExplosionRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield_render_system import SphereMagicShieldRenderSystem
from roguelike_game.ecs.systems.combat.spells.puddle_system import PuddleSystem
from roguelike_game.ecs.systems.rendering.combat.spells.puddle_render_system import PuddleRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.vortex_field_render_system import VortexFieldRenderSystem
from roguelike_game.ecs.systems.rendering.combat.spells.mine_render_system import MineRenderSystem
from roguelike_game.ecs.systems.status.dot_system import DoTSystem
from roguelike_game.ecs.systems.combat.spells.mine_system import MineSystem
from roguelike_game.ecs.systems.combat.spells.chain_lightning_system import ChainLightningSystem
from roguelike_game.ecs.systems.combat.spells.force_field_system import ForceFieldSystem
from roguelike_game.ecs.systems.combat.spells.cone_breath_system import ConeBreathSystem
from roguelike_game.ecs.systems.combat.spells.meteor_shower_system import MeteorShowerSystem
from roguelike_game.ecs.systems.combat.spells.meteor_fall_system import MeteorFallSystem
from roguelike_game.ecs.systems.combat.spells.summon_system import SummonSystem
from roguelike_game.ecs.systems.combat.spells.totem_system import TotemSystem
from roguelike_game.ecs.systems.combat.spells.wall_system import WallSystem
from roguelike_game.ecs.systems.buildings_portal_system import BuildingPortalSystem
from roguelike_game.ecs.systems.status.stun_system import StunSystem

from roguelike_game.ecs.systems.rendering.particles.particle_render_system import ParticleRenderSystem
from roguelike_game.ecs.systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from roguelike_game.ecs.systems.rendering.flash_system import FlashSystem
from roguelike_game.ecs.systems.rendering.trail_system import TrailSystem
from roguelike_game.ecs.systems.rendering.ribbon_system import RibbonSystem
from roguelike_game.ecs.systems.fsm.fsm_system import FSMSystem
from roguelike_game.ecs.systems.combat.spells.dash_system import DashSystem
from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem
from roguelike_game.ecs.systems.combat.death_system import DeathSystem
from roguelike_game.ecs.systems.spawner.spawner_damage_system import SpawnerDamageSystem
from roguelike_game.ecs.systems.combat.building_damage_system import BuildingDamageSystem
from roguelike_game.ecs.systems.combat.spells.lightning_system import LightningSystem
from roguelike_editors.fsm.debug.entities_debug_system import EntitiesDebugSystem
from roguelike_game.ecs.systems.map.expansion_system import ExpansionSystem
from roguelike_game.ecs.systems.experience.experience_system import ExperienceSystem
from roguelike_game.ecs.systems.rendering.magic_spell_bar_system import MagicSpellBarSystem
from roguelike_game.ecs.systems.physics.coin_pickup_system import CoinPickupSystem
from roguelike_game.ecs.systems.core.spawn_stabilization_system import SpawnStabilizationSystem
from roguelike_game.ecs.systems.core.npc_restore_system import NpcRestoreSystem
from roguelike_game.ecs.systems.core.npc_respawn_system import NpcRespawnSystem
from roguelike_game.ecs.systems.experience.orb_attraction_system import OrbAttractionSystem
from roguelike_game.ecs.systems.inventory.inventory_init_system import InventoryInitSystem
from roguelike_game.ecs.systems.inventory.death_drop_system import DeathDropSystem
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.ecs.systems.items.consume_system import ConsumeSystem
from roguelike_game.ecs.systems.items.teleport_system import TeleportSystem as ItemTeleportSystem
from roguelike_game.ecs.systems.inventory.inventory_transfer_system import InventoryTransferSystem
from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.ecs.systems.inventory.drop_despawn_system import DropDespawnSystem
from roguelike_game.ecs.systems.inventory.drop_drag_system import DropDragSystem
from roguelike_game.ecs.systems.inventory.inventory_drag_system import InventoryDragSystem
from roguelike_game.ecs.systems.inventory.inventory_ui_system import InventoryUISystem
from roguelike_game.ecs.systems.spawner.spawner_placement_system import SpawnerPlacementSystem
from roguelike_game.ecs.systems.spawner.spawner_trigger_system import SpawnerTriggerSystem
from roguelike_game.ecs.systems.spawner.spawner_runtime import SpawnerRuntimeSystem
from roguelike_game.ecs.systems.rendering.temp_z_layer_system import TempZLayerSystem

from roguelike_game.ecs.systems.rendering.drop_hover_system import DropHoverRenderSystem
from roguelike_game.ecs.systems.rendering.grayscale_render_system import GrayscaleRenderSystem
from roguelike_game.ecs.systems.rendering.resurrection_area_system import ResurrectionAreaSystem
from roguelike_game.ecs.systems.rendering.experience_render_system import ExperienceRenderSystem
from roguelike_game.ecs.systems.rendering.magic_spell_bar_render_system import MagicSpellBarRenderSystem
from roguelike_game.ecs.systems.rendering.spawner_debug_system import SpawnerDebugRenderSystem
from roguelike_game.ecs.systems.audio.audio_system import AudioSystem
from roguelike_game.ecs.systems.chat.chat_proximity_system import ChatProximitySystem
from roguelike_game.ecs.systems.chat.chat_router_system import ChatRouterSystem
from roguelike_game.ecs.systems.chat.chat_ui_system import ChatUISystem
from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem
from roguelike_game.ecs.systems.vendors.vendor_ui_system import VendorUISystem
from roguelike_game.ecs.systems.rendering.chat_proximity_render_system import ChatProximityRenderSystem
from roguelike_game.ecs.systems.rendering.chat_bubble_render_system import ChatBubbleRenderSystem
from roguelike_game.ecs.systems.abilities.dash_resource_system import DashResourceSystem
from roguelike_game.ecs.systems.abilities.mana_regen_system import ManaRegenSystem
from roguelike_game.ecs.systems.rendering.dash_bar_render_system import DashBarRenderSystem
from roguelike_game.ecs.systems.abilities.combo_system import ComboSystem
from roguelike_game.ecs.systems.rendering.combo_bar_render_system import ComboBarRenderSystem
from roguelike_game.ecs.systems.rendering.toast_render_system import ToastRenderSystem
from roguelike_game.ecs.systems.rendering.target_hud_render_system import TargetHudRenderSystem
from roguelike_game.ecs.systems.rendering.cast_outline_render_system import CastOutlineRenderSystem

logger = logging.getLogger(__name__)

# Cache for ECS z-order config
_ECS_Z_CFG: dict | None = None

def _load_ecs_z_config() -> dict:
    global _ECS_Z_CFG
    if _ECS_Z_CFG is not None:
        return _ECS_Z_CFG
    try:
        path = os.path.join(os.path.dirname(__file__), 'ecs-z-order.json')
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
            if isinstance(data, dict):
                _ECS_Z_CFG = data
                return data
    except Exception:
        # Silent fallback to defaults
        pass
    _ECS_Z_CFG = {}
    return _ECS_Z_CFG

def _ecs_z_for(cls_name: str, default_index: int) -> int:
    """Return z for a render system class. Default is derived from index to preserve order."""
    cfg = _load_ecs_z_config()
    try:
        entry = cfg.get(cls_name, None)
        if isinstance(entry, dict) and 'z' in entry:
            return int(entry.get('z'))
    except Exception:
        pass
    # Default z preserves original order while allowing configured items to move
    return (default_index + 1) * 10

def get_update_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de actualización.
    """
    return [
        # Spawner systems (runtime M1)
        SpawnerPlacementSystem, SpawnerTriggerSystem, SpawnerRuntimeSystem,
        # Antes de procesar SpawnRequest, generar requests faltantes por NPCs persistidos
        NpcRespawnSystem,
        # Process spawn requests and immediately stabilize overlapped spawns
        SpawnSystem,
        SpawnStabilizationSystem,
        TimedDespawnSystem,
        # Apply restored state (position/hp) once entities exist and are stabilized
        NpcRestoreSystem,
        # Player & input
        PlayerFacingSystem, FacingSystem, DropDragSystem, InputSystem, StunSystem, ChatProximitySystem, DashResourceSystem, ManaRegenSystem, ForceFieldSystem,
        MovementCollisionSystem,
        # Buildings portals (trigger after movement/collision resolution)
        BuildingPortalSystem, ItemTeleportSystem,
        # Combat & spells (AutoCast antes de SpellCasting para encolar intents)
        MeleeCombatSystem, AutoCastSystem, SpellCastingSystem, ArcaneFlameSystem, SmokeSystem, PuddleSystem, MineSystem, WallSystem, TotemSystem, SummonSystem, DoTSystem, SmokeEmitterSystem, SphereMagicShieldSystem, TeleportSystem, FireworkLaunchSystem, AuraSystem, MeteorShowerSystem, MeteorFallSystem, ParticleSystem, ExplosionSystem, FireballTrailEmitterSystem, LaserBeamEmitterSystem, HealingAuraEmitterSystem, SlashEmitterSystem, DashEmitterSystem, LightningEmitterSystem, VortexFieldEmitterSystem, BoomerangGlowEmitterSystem, BoomerangSystem, FireballSystem, ChainLightningSystem, LightningSystem, ConeBreathSystem, DashSystem, HitboxSystem, SpawnerDamageSystem, DeathSystem, FSMSystem, ComboSystem, BuildingDamageSystem,
        TrailSystem, RibbonSystem,
        AnimationSystem, FlashSystem, TorchLightSystem, LightingSyncSystem,
        # Inventory & pickups (keyboard drop disabled; only drag-and-drop allowed)
        InventoryInitSystem, DeathDropSystem, InventoryPickupSystem, ConsumeSystem, InventoryTransferSystem, InventoryDragSystem, MapLoadDropsSystem, TempZLayerSystem, DropDespawnSystem, CoinPickupSystem, OrbAttractionSystem, ExperienceSystem, MagicSpellBarSystem, ExpansionSystem,
        # Chat & Trade
        ChatRouterSystem, VendorTradeSystem,
        # Audio bridge
        AudioSystem,
    ]

def get_render_system_classes():
    """
    Devuelve la lista de clases de sistemas que se ejecutan en la fase de renderizado.
    Se añade dinámicamente SpawnDebug y DeathTimerDebug si estamos en DEBUG.
    """
    base = [
        HealthBarSystem, DashBarRenderSystem, ManaBarRenderSystem, ManaRegenAuraRenderSystem, GodmodeAuraRenderSystem, SpawnerAuraRenderSystem, NamePlateSystem, ChatBubbleRenderSystem, ExperienceRenderSystem, ComboBarRenderSystem, MagicSpellBarRenderSystem,
        FireballRenderSystem, ArcaneFlameRenderSystem, FireworkLaunchRenderSystem, SmokeRenderSystem, PuddleRenderSystem, WallRenderSystem, TotemRenderSystem, MeteorShowerRenderSystem, VortexFieldRenderSystem, MineRenderSystem, SmokeEmitterRenderSystem, SphereMagicShieldRenderSystem, TeleportRenderSystem, ParticleRenderSystem, ParticlePresetRenderSystem, LightningRenderSystem, BoomerangRenderSystem, ConeBreathRenderSystem, MeteorFallRenderSystem,
        DeathTimerBarSystem,
        # DropRenderSystem removed: drops rendered via RenderSystem
    ]
    # Render systems comunes
    # Overlay unificado del FSM Editor (se activa/desactiva internamente con F12)
    base.append(EntitiesDebugSystem)
    base.append(GrayscaleRenderSystem)
    base.append(ResurrectionAreaSystem)
    base.append(CastOutlineRenderSystem)
    base.append(ExperienceRenderSystem)
    # HUD textual overlay (bottom-left): HP/MP values
    base.append(HUDStatsRenderSystem)
    # HUD de objetivo (centrado arriba)
    base.append(TargetHudRenderSystem)
    # Otros sistemas de render (eliminados FlashSystem y TrailSystem de render)
    base.append(DropHoverRenderSystem)
    # Halo de proximidad de chat (círculo amarillo)
    base.append(ChatProximityRenderSystem)
    base.append(InventoryUISystem)
    # Toasts HUD (esquina inferior derecha)
    base.append(ToastRenderSystem)
    # Chat UI overlay
    base.append(ChatUISystem)
    # Vendor UI overlay (panel de comercio junto al chat)
    base.append(VendorUISystem)
    # Spawner debug overlay always visible above game objects
    base.append(SpawnerDebugRenderSystem)
    # FlashSystem y TrailSystem son sistemas de update, no deben ir en render
    # Order by external ECS z-order with stable fallback
    decorated = [(cls, _ecs_z_for(getattr(cls, '__name__', str(cls)), i), i) for i, cls in enumerate(base)]
    ordered = [cls for (cls, _z, _i) in sorted(decorated, key=lambda t: (t[1], t[2]))]
    return ordered