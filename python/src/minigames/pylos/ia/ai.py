from __future__ import annotations

from dataclasses import dataclass
from math import inf
from typing import Optional, Tuple, List

from pylos.model import Board, Cell, GamePhase, GameState, PlayerId, TurnSubphase


@dataclass
class HeuristicWeights:
    reserve: float = 0.5
    immediate_square: float = 3.0
    future_square: float = 1.0
    upper_control: float = 0.8


class PylosAI:
    """Basic Minimax with alpha-beta pruning for Pylos.

    Heuristic considers:
      - Number of marbles in reserve (15 - placed).
      - Possibility to form immediate squares (3 + 1 empty supported).
      - Future square potential (>=2 and no opponent marbles in that 2x2).
      - Control of upper levels (marbles in higher layers weighted more).
    """

    def __init__(self, ai_player: PlayerId = 2, depth: int = 3, weights: Optional[HeuristicWeights] = None) -> None:
        self.ai_player: PlayerId = ai_player
        self.depth: int = depth
        self.w: HeuristicWeights = weights or HeuristicWeights()

    def set_depth(self, depth: int) -> None:
        self.depth = max(1, int(depth))

    # --- Public API ---
    def choose_action(self, state: GameState) -> Tuple[str, Tuple[Cell, ...]] | None:
        """Choose a full-rules action for the current player.

        Returns a tuple (kind, args):
          - ("place", (dst,))
          - ("climb", (src, dst))
        Returns None if no legal actions.
        """
        best_score = -inf
        best_action: Tuple[str, Tuple[Cell, ...]] | None = None
        actions = self._enumerate_actions(state)
        if not actions:
            return None
        alpha, beta = -inf, inf
        for kind, cells in actions:
            nxt = state.clone()
            if kind == "place":
                if not nxt.attempt_place(cells[0]):
                    continue
                self._apply_removals_if_any(nxt, nxt.current_player)
            elif kind == "climb":
                if not nxt.attempt_climb(cells[0], cells[1]):
                    continue
                self._apply_removals_if_any(nxt, nxt.current_player)
            score = self._minimax(nxt, self.depth - 1, alpha, beta, maximizing=False)
            if score > best_score:
                best_score = score
                best_action = (kind, cells)
            alpha = max(alpha, best_score)
            if beta <= alpha:
                break
        return best_action

    # --- Minimax ---
    def _minimax(self, state: GameState, depth: int, alpha: float, beta: float, maximizing: bool) -> float:
        # Terminal
        if state.phase == GamePhase.ENDED:
            if state.winner is None:
                return 0.0
            return (1e6 if state.winner == self.ai_player else -1e6)
        if depth == 0:
            return self._evaluate(state)

        actions = self._enumerate_actions(state)
        if not actions:
            return 0.0

        if maximizing:
            value = -inf
            for kind, cells in actions:
                nxt = state.clone()
                ok = False
                if kind == "place":
                    ok = nxt.attempt_place(cells[0])
                else:
                    ok = nxt.attempt_climb(cells[0], cells[1])
                if not ok:
                    continue
                self._apply_removals_if_any(nxt, nxt.current_player)
                value = max(value, self._minimax(nxt, depth - 1, alpha, beta, maximizing=False))
                alpha = max(alpha, value)
                if beta <= alpha:
                    break
            return value
        else:
            value = inf
            for kind, cells in actions:
                nxt = state.clone()
                ok = False
                if kind == "place":
                    ok = nxt.attempt_place(cells[0])
                else:
                    ok = nxt.attempt_climb(cells[0], cells[1])
                if not ok:
                    continue
                self._apply_removals_if_any(nxt, nxt.current_player)
                value = min(value, self._minimax(nxt, depth - 1, alpha, beta, maximizing=True))
                beta = min(beta, value)
                if beta <= alpha:
                    break
            return value

    # --- Heuristic ---
    def _evaluate(self, state: GameState) -> float:
        board = state.board
        me, opp = self.ai_player, (1 if self.ai_player == 2 else 2)

        # Reserves: 15 per player in Pylos
        counts = board.count_player_marbles()
        reserve_me = 15 - counts.get(me, 0)
        reserve_opp = 15 - counts.get(opp, 0)

        imm_me, fut_me = self._square_potentials(board, me)
        imm_opp, fut_opp = self._square_potentials(board, opp)

        upper_me = self._upper_control(board, me)
        upper_opp = self._upper_control(board, opp)

        score = 0.0
        score += self.w.reserve * (reserve_me - reserve_opp)
        score += self.w.immediate_square * (imm_me - imm_opp)
        score += self.w.future_square * (fut_me - fut_opp)
        score += self.w.upper_control * (upper_me - upper_opp)
        return score

    def _square_potentials(self, board: Board, player: PlayerId) -> Tuple[int, int]:
        """Return (immediate, future) square potentials for `player`.

        Immediate: 3 owned + 1 empty (and that empty is a valid placement).
        Future: at least 2 owned, no opponent marbles in the 2x2 block.
        """
        immediate = 0
        future = 0
        for layer in range(0, 3):  # 0..2 layers have 2x2 squares
            size = Board.LAYER_SIZES[layer]
            for y in range(size - 1):
                for x in range(size - 1):
                    cells = [
                        Cell(layer, x, y),
                        Cell(layer, x + 1, y),
                        Cell(layer, x, y + 1),
                        Cell(layer, x + 1, y + 1),
                    ]
                    owners = [board.get(c) for c in cells]
                    if any(o is not None and o not in (1, 2) for o in owners):
                        continue
                    me_cnt = sum(1 for o in owners if o == player)
                    opp_cnt = sum(1 for o in owners if o not in (None, player))
                    empties = [cells[i] for i, o in enumerate(owners) if o is None]
                    if opp_cnt > 0:
                        continue
                    if me_cnt == 3 and len(empties) == 1:
                        # Only count if the empty can be placed now
                        if board.valid_placement(empties[0]):
                            immediate += 1
                        else:
                            # Not placeable yet, still counts as future
                            future += 1
                    elif me_cnt >= 2:
                        future += 1
        return immediate, future

    def _upper_control(self, board: Board, player: PlayerId) -> float:
        """Weighted sum of marbles on higher layers.

        Weights increase with layer index to reward upper-level control.
        """
        weights = [1.0, 2.0, 4.0, 8.0]
        score = 0.0
        for layer, size in enumerate(Board.LAYER_SIZES):
            for y in range(size):
                for x in range(size):
                    c = Cell(layer, x, y)
                    if board.get(c) == player:
                        score += weights[layer]
        return score

    # --- Utils ---
    def _enumerate_actions(self, state: GameState) -> List[Tuple[str, Tuple[Cell, ...]]]:
        """List legal actions for the current player: places and climbs."""
        me = state.current_player
        actions: List[Tuple[str, Tuple[Cell, ...]]] = []
        # Placements
        places = state.board.valid_moves()
        places.sort(key=lambda c: (c.layer, -c.y, -c.x), reverse=True)
        actions.extend(("place", (dst,)) for dst in places)
        # Climbs: from free marbles to higher valid placements
        free = state.board.free_marbles(me)
        # For ordering, prefer higher destination layers first
        for src in free:
            for dst in places:
                if dst.layer > src.layer:  # must go up
                    actions.append(("climb", (src, dst)))
        return actions

    def _apply_removals_if_any(self, state: GameState, actor: PlayerId) -> None:
        """Greedily remove up to state.removals_allowed own free marbles.

        This is a simplification: we don't branch over removal choices to keep
        the branching factor manageable. We remove pieces that improve eval most.
        """
        if state.subphase != TurnSubphase.REMOVAL or state.removals_allowed <= 0:
            return
        # Greedy loop up to allowed
        for _ in range(state.removals_allowed):
            choices = state.board.free_marbles(actor)
            if not choices:
                break
            best_cell = None
            best_value = -inf
            for c in choices:
                tmp = state.clone()
                if tmp.board.remove_at(c) != actor:
                    continue
                val = self._evaluate(tmp)
                if val > best_value:
                    best_value = val
                    best_cell = c
            if best_cell is None:
                break
            state.remove_own_free(best_cell)
            if state.subphase != TurnSubphase.REMOVAL:
                break
        # Ensure end of removal phase
        if state.subphase == TurnSubphase.REMOVAL:
            state.finish_removal()

    # --- Public removal helper ---
    def perform_removals(self, state: GameState) -> None:
        """Perform removal phase for the AI player greedily."""
        self._apply_removals_if_any(state, self.ai_player)
