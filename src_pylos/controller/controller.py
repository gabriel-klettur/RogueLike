from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

import pygame

from src_pylos.model import Cell, GameState, GamePhase
from src_pylos.view import PylosView
from src_pylos.IA import PylosAI


@dataclass
class InputState:
    """Transient input state managed by the controller."""

    hovered: Optional[Cell] = None
    running: bool = True
    selected_src: Optional[Cell] = None
    show_holes: bool = False
    show_indices: bool = False
    dragging: bool = False  # RMB drag to climb


class PylosController:
    """Translate user input events into game state changes.

    Also integrates an AI opponent (Player 2) using Minimax with alpha-beta.
    Difficulty is controlled by search depth (1-5) via number keys.
    """

    def __init__(
        self,
        state: GameState,
        view: PylosView,
        ai: Optional[PylosAI] = None,
        ai_enabled: bool = True,
        ai_player: int = 2,
    ) -> None:
        self.state = state
        self.view = view
        self.input = InputState()
        self.ai = ai or PylosAI(ai_player=ai_player, depth=3)
        self.ai_enabled = ai_enabled
        self.ai.ai_player = ai_player
        # Request to return to menu
        self._request_menu: bool = False

    def handle_event(self, event: pygame.event.Event) -> None:
        if event.type == pygame.QUIT:
            self.input.running = False
            return

        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                self.input.running = False
                return
            if event.key == pygame.K_r:
                # Go back to main menu
                self._request_menu = True
                return
            # Difficulty shortcuts 1..5 set AI depth
            if event.key in (pygame.K_1, pygame.K_2, pygame.K_3, pygame.K_4, pygame.K_5):
                new_depth = int(event.unicode) if event.unicode.isdigit() else 3
                self.ai.set_depth(new_depth)
                return
            # Finish removal early (only if allowed by rules)
            if event.key == pygame.K_SPACE:
                if hasattr(self.state, "can_finish_removal") and self.state.can_finish_removal():
                    self.state.finish_removal()
                return
            # Toggle show holes
            if event.key == pygame.K_h:
                self.input.show_holes = not self.input.show_holes
                return
            # Toggle show indices
            if event.key == pygame.K_i:
                self.input.show_indices = not self.input.show_indices
                return

        if event.type == pygame.MOUSEMOTION:
            pos = pygame.mouse.get_pos()
            # In REMOVAL, pick the nearest own free marble (avoid empty upper cells)
            sub = getattr(self.state, "subphase", None)
            if sub is not None and sub.name == "REMOVAL":
                cand = self.view.pick_removal_candidate(pos, self.state)
                self.input.hovered = cand
            else:
                self.input.hovered = self.view.pick_cell(pos, self.state.board)
            return

        if event.type == pygame.MOUSEBUTTONDOWN:
            # If game ended and user clicks on the win banner, go to menu
            if self.state.phase == GamePhase.ENDED:
                rect = self.view.get_win_banner_rect()
                if rect is not None and rect.collidepoint(pygame.mouse.get_pos()):
                    self._request_menu = True
                    return
            # Right-click: select nearest own FREE marble under cursor (robust) and start drag (ACTION only)
            if event.button == 3:  # RMB
                pos = pygame.mouse.get_pos()
                # Only allow selecting/dragging during ACTION subphase
                if self.is_human_action_turn():
                    candidate = self.view.pick_removal_candidate(pos, self.state)
                    if candidate is not None:
                        self.input.selected_src = candidate
                        self.input.dragging = True
                        return
                # Deselect otherwise
                self.input.selected_src = None
                return

        if event.type == pygame.MOUSEBUTTONUP:
            # RMB release: if dragging and hovered is a valid higher destination, perform climb
            if event.button == 3:  # RMB
                if self.input.dragging:
                    self.input.dragging = False
                    if self.input.selected_src is not None and self.input.hovered is not None:
                        if self.state.attempt_climb(self.input.selected_src, self.input.hovered):
                            self.input.selected_src = None
                    return

            # Left-click actions
            if event.button == 1:
                if self.state.phase != GamePhase.PLAYING:
                    return
                # If clicking the Show Holes button, toggle
                btn = self.view.get_showholes_button_rect()
                if btn is not None and btn.collidepoint(pygame.mouse.get_pos()):
                    self.input.show_holes = not self.input.show_holes
                    return
                # If clicking the Show Indices button, toggle
                idx_btn = self.view.get_indices_button_rect()
                if idx_btn is not None and idx_btn.collidepoint(pygame.mouse.get_pos()):
                    self.input.show_indices = not self.input.show_indices
                    return
                # Removal subphase: remove own free
                if hasattr(self.state, "subphase") and str(self.state.subphase) and getattr(self.state, "removals_allowed", 0) > 0 and self.state.subphase.name == "REMOVAL":
                    # Check End Removal buttons
                    pos = pygame.mouse.get_pos()
                    for pid in (1, 2):
                        rect = self.view.get_end_removal_button_rect(pid)
                        if rect is not None and rect.collidepoint(pos):
                            if self.state.current_player == pid and getattr(self.state, "can_finish_removal", lambda: False)():
                                self.state.finish_removal()
                            return
                    # Use the pre-filtered hovered cell (own & free) to avoid picking empty upper-layer cells
                    if self.input.hovered is not None:
                        self.state.remove_own_free(self.input.hovered)
                    return
                # If a source is selected and hovered is a valid dst above, attempt climb
                if self.input.selected_src is not None and self.input.hovered is not None:
                    if self.state.attempt_climb(self.input.selected_src, self.input.hovered):
                        self.input.selected_src = None
                    return
                # Otherwise, attempt placement if hovered is valid
                if self.input.hovered is not None:
                    self.state.attempt_place(self.input.hovered)
                return

    def is_running(self) -> bool:
        return self.input.running

    def request_menu(self) -> bool:
        return self._request_menu

    def hovered_cell(self) -> Optional[Cell]:
        return self.input.hovered

    def update(self, dt: float) -> None:
        """Per-frame update: if AI is enabled and it's AI's turn, make an action.

        Also handle AI removal subphase greedily.
        """
        if self.state.phase != GamePhase.PLAYING:
            return
        if not self.ai_enabled:
            return
        if self.state.current_player != self.ai.ai_player:
            return
        # If in removal subphase, let AI perform removals
        if getattr(self.state, "subphase", None) and self.state.subphase.name == "REMOVAL":
            self.ai.perform_removals(self.state)
            return
        # Otherwise choose an action
        action = self.ai.choose_action(self.state)
        if action is None:
            return
        kind, cells = action
        if kind == "place":
            self.state.attempt_place(cells[0])
        else:
            self.state.attempt_climb(cells[0], cells[1])
        # refresh hover after AI move
        pos = pygame.mouse.get_pos()
        self.input.hovered = self.view.pick_cell(pos, self.state.board)

    def info_text(self) -> str:
        role = "ON" if self.ai_enabled else "OFF"
        return f"AI:{role} depth:{self.ai.depth} (P{self.ai.ai_player})"

    def selected_src(self) -> Optional[Cell]:
        return self.input.selected_src

    def is_ai_p2(self) -> bool:
        return self.ai_enabled and self.ai.ai_player == 2

    def is_human_action_turn(self) -> bool:
        """True if current turn is a human player's ACTION subphase."""
        if self.state.phase != GamePhase.PLAYING:
            return False
        # Import here to avoid circular (already imported at top): GamePhase has TurnSubphase
        try:
            sub = getattr(self.state, "subphase", None)
            if sub is None or sub.name != "ACTION":
                return False
        except Exception:
            return False
        if not self.ai_enabled:
            return True
        return self.state.current_player != self.ai.ai_player

    def cursor_player_id(self) -> Optional[int]:
        """Return player id to use for cursor preview if human ACTION turn, else None."""
        return self.state.current_player if self.is_human_action_turn() else None

    def show_holes(self) -> bool:
        return self.input.show_holes

    def show_indices(self) -> bool:
        return self.input.show_indices

    def is_human_removal_turn(self) -> bool:
        """True if current turn is a human player's REMOVAL subphase."""
        if self.state.phase != GamePhase.PLAYING:
            return False
        sub = getattr(self.state, "subphase", None)
        if sub is None or sub.name != "REMOVAL":
            return False
        if not self.ai_enabled:
            return True
        return self.state.current_player != self.ai.ai_player

    def removal_cursor_player_id(self) -> Optional[int]:
        """Return player id if human REMOVAL subphase, else None."""
        return self.state.current_player if self.is_human_removal_turn() else None
