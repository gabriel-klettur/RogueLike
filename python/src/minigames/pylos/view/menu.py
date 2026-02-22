from __future__ import annotations

from dataclasses import dataclass
from typing import List, Tuple

import pygame

from .view import ViewConfig


@dataclass
class GameConfig:
    ai_enabled: bool = True
    ai_player: int = 2  # 1 or 2
    ai_depth: int = 3   # 1..5
    starting_player: int = 1  # 1 or 2
    game_mode: str = "Experto"  # "Niño" or "Experto"


class ConfigMenu:
    """Simple configuration menu for Pylos.

    Controls:
      - Up/Down: select option
      - Left/Right: change value
      - Enter: start game
      - Esc: quit
    """

    def __init__(self, screen: pygame.Surface, cfg: ViewConfig) -> None:
        self.screen = screen
        self.cfg = cfg
        self.font_title = pygame.font.SysFont("consolas", 36)
        self.font = pygame.font.SysFont("consolas", 24)
        self.options: List[Tuple[str, str]] = []  # label -> value str
        self.selected_index = 0
        self.config = GameConfig()

    def run(self) -> GameConfig:
        clock = pygame.time.Clock()
        running = True
        while running:
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    pygame.quit()
                    raise SystemExit(0)
                if event.type == pygame.KEYDOWN:
                    if event.key == pygame.K_ESCAPE:
                        pygame.quit()
                        raise SystemExit(0)
                    if event.key == pygame.K_RETURN:
                        return self.config
                    if event.key == pygame.K_UP:
                        self.selected_index = (self.selected_index - 1) % 5
                    if event.key == pygame.K_DOWN:
                        self.selected_index = (self.selected_index + 1) % 5
                    if event.key == pygame.K_LEFT:
                        self._adjust_option(-1)
                    if event.key == pygame.K_RIGHT:
                        self._adjust_option(+1)

            self._draw()
            pygame.display.flip()
            clock.tick(60)
        return self.config

    def _adjust_option(self, delta: int) -> None:
        if self.selected_index == 0:
            # Opponent: Human/AI
            self.config.ai_enabled = not self.config.ai_enabled
        elif self.selected_index == 1:
            # AI plays as: 1 or 2
            self.config.ai_player = 1 if self.config.ai_player == 2 else 2
        elif self.selected_index == 2:
            # AI depth: 1..5
            self.config.ai_depth = max(1, min(5, self.config.ai_depth + delta))
        elif self.selected_index == 3:
            # Starting player
            self.config.starting_player = 1 if self.config.starting_player == 2 else 2
        elif self.selected_index == 4:
            # Game mode: Niño / Experto
            self.config.game_mode = "Niño" if self.config.game_mode == "Experto" else "Experto"

    def _draw(self) -> None:
        self.screen.fill(self.cfg.bg_color)
        title = self.font_title.render("Pylos — Configuración", True, self.cfg.text_color)
        self.screen.blit(title, (self.cfg.width // 2 - title.get_width() // 2, 80))

        items = [
            ("Oponente", "IA" if self.config.ai_enabled else "Humano"),
            ("IA juega como", f"Jugador {self.config.ai_player}"),
            ("Profundidad IA", str(self.config.ai_depth)),
            ("Comienza", f"Jugador {self.config.starting_player}"),
            ("Modo de juego", self.config.game_mode),
        ]

        start_hint = self.font.render("Enter — Comenzar  |  Esc — Salir", True, self.cfg.text_color)
        self.screen.blit(start_hint, (self.cfg.width // 2 - start_hint.get_width() // 2, self.cfg.height - 120))

        y = 180
        for idx, (label, value) in enumerate(items):
            is_sel = (idx == self.selected_index)
            color = (255, 235, 59) if is_sel else self.cfg.text_color
            text = self.font.render(f"{label}: {value}", True, color)
            self.screen.blit(text, (self.cfg.width // 2 - text.get_width() // 2, y))
            y += 44
