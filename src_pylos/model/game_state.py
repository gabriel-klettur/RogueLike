from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum, auto
from typing import Optional, List

from .board import Board, Cell, PlayerId


class GamePhase(Enum):
    """Phases of the Pylos mini-game."""

    PLAYING = auto()
    ENDED = auto()


class TurnSubphase(Enum):
    """Subphases within a player's turn for full Pylos rules."""

    ACTION = auto()   # Place a new marble or climb (move up)
    REMOVAL = auto()  # Optional removal of up to 2 free marbles (<= squares formed)


@dataclass
class GameState:
    """Holds the game state, including board, current player, and phase.

    Simplified Pylos rules: only placements with proper support. No removals or
    moves. The game ends when the apex (top cell) is filled or no valid moves
    remain. In case of apex filled, the last player to move wins. Otherwise it's
    a stalemate (no winner).
    """

    board: Board = field(default_factory=Board)
    current_player: PlayerId = 1
    phase: GamePhase = GamePhase.PLAYING
    subphase: TurnSubphase = TurnSubphase.ACTION
    winner: Optional[PlayerId] = None
    removals_allowed: int = 0
    removals_taken: int = 0
    last_move_destination: Optional[Cell] = None
    # Rules toggle: when False (Niño), forming a square does not grant removals
    allow_square_removal: bool = True

    def reset(self) -> None:
        self.board = Board()
        self.current_player = 1
        self.phase = GamePhase.PLAYING
        self.winner = None
        self.subphase = TurnSubphase.ACTION
        self.removals_allowed = 0
        self.removals_taken = 0
        self.last_move_destination = None
        self.allow_square_removal = True

    def clone(self) -> "GameState":
        """Return a shallow copy of the game state with a cloned board.

        Intended for AI simulations where we need to try hypothetical moves
        without mutating the real game state.
        """
        return GameState(
            board=self.board.clone(),
            current_player=self.current_player,
            phase=self.phase,
            winner=self.winner,
            subphase=self.subphase,
            removals_allowed=self.removals_allowed,
            removals_taken=self.removals_taken,
            last_move_destination=self.last_move_destination,
            allow_square_removal=self.allow_square_removal,
        )

    def switch_turn(self) -> None:
        self.current_player = 2 if self.current_player == 1 else 1
        self.subphase = TurnSubphase.ACTION
        self.removals_allowed = 0
        self.last_move_destination = None
        # If the next player cannot act (no reserve placements and no climbs), they lose
        self._check_unavailability_loss()

    def check_end_conditions(self) -> None:
        if self.board.is_apex_filled():
            # Whoever placed on apex (or last placed) is the winner
            self.phase = GamePhase.ENDED
            self.winner = self.board.last_player_to_move
            return
        # Additional end condition: if the current player cannot perform any legal action,
        # they lose immediately (reserve empty AND no valid climbs).
        self._check_unavailability_loss()

    def _has_any_action(self, player: PlayerId) -> bool:
        """Return True if `player` has at least one legal action (place or climb)."""
        # 1) Can place from reserve?
        if self.reserve_remaining(player) > 0 and self.board.valid_moves():
            return True
        # 2) Can climb? Any free marble to any higher valid placement
        free = self.board.free_marbles(player)
        if not free:
            return False
        places = self.board.valid_moves()
        for src in free:
            for dst in places:
                if dst.layer > src.layer:
                    return True
        return False

    def _check_unavailability_loss(self) -> None:
        if self.phase != GamePhase.PLAYING:
            return
        cur = self.current_player
        # Standard rule: a player loses only if they have no legal action
        # (no placements AND no climbs)
        if not self._has_any_action(cur):
            self.phase = GamePhase.ENDED
            self.winner = 2 if cur == 1 else 1

    # --- Full-rules actions ---
    def attempt_place(self, cell: Cell) -> bool:
        """Place a new marble. If squares are formed, enter REMOVAL subphase.

        Returns True if action executed.
        """
        if self.phase != GamePhase.PLAYING or self.subphase != TurnSubphase.ACTION:
            return False
        # Enforce reserve limit per player (15 marbles total)
        if self.reserve_remaining(self.current_player) <= 0:
            return False
        if not self.board.place(self.current_player, cell):
            return False
        self.last_move_destination = cell
        self._post_action_square_and_phase()
        return True

    def attempt_climb(self, src: Cell, dst: Cell) -> bool:
        """Move one of the player's free marbles upward. Then check squares/removal.

        Returns True if action executed.
        """
        if self.phase != GamePhase.PLAYING or self.subphase != TurnSubphase.ACTION:
            return False
        if not self.board.move(self.current_player, src, dst):
            return False
        self.last_move_destination = dst
        self._post_action_square_and_phase()
        return True

    def _post_action_square_and_phase(self) -> None:
        # After place or climb, check end and squares
        self.check_end_conditions()
        if self.phase != GamePhase.PLAYING:
            return
        formed_square = 0
        formed_line = 0
        if self.last_move_destination is not None:
            formed_square = self.board.squares_created_by(self.current_player, self.last_move_destination)
            formed_line = self.board.lines_created_by(self.current_player, self.last_move_destination)
        if self.allow_square_removal and (formed_square > 0 or formed_line > 0):
            # Completing a square or an alignment line grants optional removal of up to 2 free marbles
            self.removals_allowed = 2
            self.removals_taken = 0
            self.subphase = TurnSubphase.REMOVAL
        else:
            self.switch_turn()

    def remove_own_free(self, cell: Cell) -> bool:
        """During REMOVAL subphase: remove one of current player's free marbles."""
        if self.phase != GamePhase.PLAYING or self.subphase != TurnSubphase.REMOVAL:
            return False
        if self.removals_allowed <= 0:
            return False
        if self.board.get(cell) != self.current_player:
            return False
        if not self.board.is_free(cell):
            return False
        removed = self.board.remove_at(cell)
        if removed == self.current_player:
            self.removals_taken += 1
            self.removals_allowed -= 1
            if self.removals_allowed == 0:
                self.finish_removal()
            return True
        return False

    def finish_removal(self) -> None:
        if self.phase != GamePhase.PLAYING or self.subphase != TurnSubphase.REMOVAL:
            return
        # En modo Experto, se deben retirar 1 o 2 bolas si es posible.
        # Permitir finalizar si ya retiró al menos 1, o si no hay ninguna libre que se pueda retirar.
        if self.removals_allowed > 0 and self.removals_taken == 0:
            # ¿Hay alguna libre propia disponible? Si no hay, permitimos finalizar.
            if any(True for _ in self.board.free_marbles(self.current_player)):
                return
        # End of turn after removals
        self.switch_turn()

    def can_finish_removal(self) -> bool:
        """Return True if UI should allow finishing the REMOVAL subphase now.

        Rules (Expert):
        - If there are still removals allowed and none were taken yet, only allow
          finishing when there are no free marbles to remove.
        - Otherwise (already removed >=1, or zero allowed), finishing is permitted.
        """
        if self.phase != GamePhase.PLAYING or self.subphase != TurnSubphase.REMOVAL:
            return False
        if self.removals_allowed > 0 and self.removals_taken == 0:
            has_any = any(True for _ in self.board.free_marbles(self.current_player))
            return not has_any
        return True

    def status_text(self) -> str:
        if self.phase == GamePhase.ENDED:
            if self.winner is None:
                return "Game Over — Stalemate"
            return f"Game Over — Player {self.winner} wins!"
        if self.subphase == TurnSubphase.REMOVAL:
            base = "remove 1-2 free marble(s)" if self.removals_taken == 0 and self.removals_allowed == 2 else f"remove up to {self.removals_allowed} free marble(s)"
            return f"Player {self.current_player}: {base} (removed {self.removals_taken})"
        return f"Player {self.current_player}: place or climb"

    # --- Utilities ---
    def reserve_remaining(self, player: PlayerId) -> int:
        """Return remaining marbles in reserve for player (out of 15)."""
        placed = self.board.count_player_marbles().get(player, 0)
        return max(0, 15 - placed)
