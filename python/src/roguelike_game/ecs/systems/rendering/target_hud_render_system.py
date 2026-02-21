import pygame
import time
from typing import Optional

from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.core.identity import Identity

# Estados opcionales para etiquetar estado del NPC
try:
    from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
except Exception:
    UnconsciousState = None  # type: ignore
try:
    from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
except Exception:
    DeathState = None  # type: ignore

try:
    from roguelike_game.factories.monster.config import MONSTER_DEFS
except Exception:
    MONSTER_DEFS = {}


class TargetHudRenderSystem:
    """
    Muestra en la parte superior centrada el HUD del último objetivo dañado por el jugador:
    - Nombre del objetivo
    - Estado (si lo hay)
    - Vida actual / máxima + barra

    Se alimenta de world.components['TargetHUD'] con campos:
      {
        'target_eid': int,
        'last_hit_time': float,
        'ttl_s': float (opcional, por defecto 3.0)
      }
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        pygame.font.init()
        # Fuentes y estilos
        try:
            self.name_font = pygame.font.SysFont("segoeui", 28)
            self.state_font = pygame.font.SysFont("segoeui", 20)
            self.hp_font = pygame.font.SysFont("consolas", 18)
        except Exception:
            self.name_font = pygame.font.SysFont(None, 28)
            self.state_font = pygame.font.SysFont(None, 20)
            self.hp_font = pygame.font.SysFont(None, 18)
        self.margin_top = 10
        self.pad_x = 12
        self.pad_y = 8
        self.bg_rgba = (0, 0, 0, 140)
        self.border_color = (255, 255, 255)
        self.name_color = (255, 255, 255)
        self.state_color = (255, 200, 120)
        self.hp_text_color = (230, 230, 230)
        self.hp_bg = (60, 60, 60)
        self.hp_fg = (220, 60, 60)
        self.border_radius = 8
        self.shadow_color = (0, 0, 0)
        self.shadow_offset = (1, 1)

    def _get_active_target(self, world) -> Optional[int]:
        data = world.components.get('TargetHUD') or {}
        if not isinstance(data, dict):
            return None
        target = data.get('target_eid')
        last = data.get('last_hit_time')
        ttl = float(data.get('ttl_s', 3.0))
        if target is None or last is None:
            return None
        if time.time() - float(last) > ttl:
            return None
        return int(target)

    def _fmt_name(self, world, eid: int) -> str:
        idc: Identity = world.components.get('Identity', {}).get(eid)
        if not idc:
            # Fallback: SpawnerConfig.template_id if this eid is a spawner
            try:
                sc = world.components.get('SpawnerConfig', {}).get(eid)
                if sc is not None:
                    tpl = getattr(sc, 'template_id', None)
                    if tpl:
                        return str(tpl)
            except Exception:
                pass
            return f"{eid}"
        base = idc.name
        try:
            alt = MONSTER_DEFS.get(base, {}).get("default_name")
            if alt:
                base = str(alt)
        except Exception:
            pass
        return base

    def _get_state_label(self, world, eid: int) -> Optional[str]:
        labels: list[str] = []
        # FSM-based label
        npc_state = world.components.get('NPCState', {}).get(eid)
        if npc_state:
            try:
                st = npc_state.fsm.current_state
            except Exception:
                st = None
            if st is not None:
                try:
                    if DeathState and isinstance(st, DeathState):
                        labels.append("Muerto")
                except Exception:
                    pass
                try:
                    if UnconsciousState and isinstance(st, UnconsciousState):
                        labels.append("Inconsciente")
                except Exception:
                    pass
                if not labels:
                    # Fallback: readable class name
                    try:
                        name = type(st).__name__
                        out = []
                        prev_lower = False
                        for ch in name:
                            if ch.isupper() and prev_lower:
                                out.append(' ')
                            out.append(ch)
                            prev_lower = ch.islower()
                        labels.append(''.join(out))
                    except Exception:
                        pass
        # Status effects: show 'Quemado' if entity has BurnComponent
        try:
            if eid in (world.components.get('BurnComponent', {}) or {}):
                labels.append("Quemado")
        except Exception:
            pass
        if not labels:
            return None
        # Deduplicate while preserving order
        seen = set()
        uniq = []
        for s in labels:
            if s not in seen:
                seen.add(s)
                uniq.append(s)
        return ' · '.join(uniq)

    def _blit_text(self, screen: pygame.Surface, font: pygame.font.Font, text: str, x: int, y: int, color) -> pygame.Surface:
        # Sombra ligera para legibilidad
        try:
            shadow = font.render(text, True, self.shadow_color)
            screen.blit(shadow, (x + self.shadow_offset[0], y + self.shadow_offset[1]))
        except Exception:
            pass
        surf = font.render(text, True, color)
        screen.blit(surf, (x, y))
        return surf

    def update(self, world, screen: pygame.Surface, camera):
        # No mostrar HUD cuando UI de menú/selector esté activa
        try:
            if bool(getattr(world, 'suppress_hud', False)):
                return
            state = getattr(world, 'state', None)
            if state and getattr(state, 'spawner_editor_active', False):
                return
        except Exception:
            pass

        target = self._get_active_target(world)
        if target is None:
            return

        comps = world.components
        hp: Health = comps.get('Health', {}).get(target)
        if hp is None:
            return
        sw, sh = screen.get_size()

        # Preparar textos
        name = self._fmt_name(world, target)
        state_label = self._get_state_label(world, target)
        hp_text = f"{max(0, int(getattr(hp, 'current_hp', 0)))} / {int(getattr(hp, 'max_hp', 0))}"

        # Dimensiones del panel
        name_w, name_h = self.name_font.size(name)
        state_w, state_h = (0, 0)
        if state_label:
            state_w, state_h = self.state_font.size(state_label)
        hp_w, hp_h = self.hp_font.size(hp_text)
        bar_w = max(220, min(480, int(sw * 0.4)))
        bar_h = 10

        content_w = max(name_w, state_w, bar_w, hp_w)
        content_h = name_h + (4 if state_label else 0) + (state_h if state_label else 0) + 6 + bar_h + 4 + hp_h
        panel_w = content_w + self.pad_x * 2
        panel_h = content_h + self.pad_y * 2

        panel_x = (sw - panel_w) // 2
        panel_y = self.margin_top

        # Fondo del panel
        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        try:
            pygame.draw.rect(panel, self.bg_rgba, panel.get_rect(), border_radius=self.border_radius)
            pygame.draw.rect(panel, (255, 255, 255, 30), panel.get_rect(), width=1, border_radius=self.border_radius)
        except TypeError:
            pygame.draw.rect(panel, self.bg_rgba, panel.get_rect())
            pygame.draw.rect(panel, (255, 255, 255, 30), panel.get_rect(), 1)

        # Blitear contenido
        cx = self.pad_x + (content_w - name_w) // 2
        cy = self.pad_y
        # Nombre
        self._blit_text(panel, self.name_font, name, cx, cy, self.name_color)
        cy += name_h
        # Estado si existe
        if state_label:
            cy += 4
            sx = self.pad_x + (content_w - state_w) // 2
            self._blit_text(panel, self.state_font, state_label, sx, cy, self.state_color)
            cy += state_h
        # Barra de vida
        cy += 6
        bx = self.pad_x + (content_w - bar_w) // 2
        ratio = 0.0
        try:
            ratio = max(0.0, float(getattr(hp, 'current_hp', 0))) / max(1.0, float(getattr(hp, 'max_hp', 1)))
        except Exception:
            ratio = 0.0
        fill_w = int(bar_w * ratio)
        pygame.draw.rect(panel, self.hp_bg, (bx, cy, bar_w, bar_h))
        pygame.draw.rect(panel, self.hp_fg, (bx, cy, fill_w, bar_h))
        pygame.draw.rect(panel, self.border_color, (bx, cy, bar_w, bar_h), 1)
        cy += bar_h
        # Texto HP
        cy += 4
        tx = self.pad_x + (content_w - hp_w) // 2
        self._blit_text(panel, self.hp_font, hp_text, tx, cy, self.hp_text_color)

        # Blit final en pantalla
        screen.blit(panel, (panel_x, panel_y))
