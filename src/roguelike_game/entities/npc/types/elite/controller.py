from roguelike_game.entities.npc.types.monster.controller import MonsterController
from .model import EliteModel
import random
import math
from roguelike_game.config_entities import ENEMY_MAX_UPDATE_DISTANCE

# Definir acciones y lógica pura optimizada
ACTIONS = ('attack', 'dodge', 'approach', 'retreat')

def get_state_logic(dx, dy, bin_size=100, max_dist=ENEMY_MAX_UPDATE_DISTANCE):
    dist_sq = dx*dx + dy*dy
    dist_bin = dist_sq // (bin_size*bin_size)
    max_bin = max_dist // bin_size
    if dist_bin > max_bin:
        dist_bin = max_bin
    sign_x = (dx>0) - (dx<0)
    sign_y = (dy>0) - (dy<0)
    return (int(dist_bin), (sign_x, sign_y))

def choose_action_logic(state, q_table, actions, epsilon):
    if random.random() < epsilon or state not in q_table:
        return actions[random.randrange(len(actions))]
    q_values = q_table[state]
    max_i, max_v = 0, q_values[0]
    for i, v in enumerate(q_values):
        if v > max_v:
            max_i, max_v = i, v
    return actions[max_i]

def update_q_logic(q_prev, action_idx, reward, q_next, alpha, gamma):
    max_next = q_next[0]
    for v in q_next:
        if v > max_next:
            max_next = v
    old_q = q_prev[action_idx]
    q_prev[action_idx] = old_q + alpha*(reward + gamma*max_next - old_q)

class EliteController(MonsterController):
    __slots__ = ('q_table','epsilon','alpha','gamma','last_state','last_action','state_space')
    bin_size = 100

    def __init__(self, model: EliteModel):
        super().__init__(model)
        # Q-learning setup
        self.epsilon = 0.2
        self.alpha = 0.5
        self.gamma = 0.9
        # Pre-populate state_space and Q-table
        max_dist_bin = ENEMY_MAX_UPDATE_DISTANCE // self.bin_size
        self.state_space = [
            (d, (sx, sy))
            for d in range(max_dist_bin+1)
            for sx in (-1, 0, 1)
            for sy in (-1, 0, 1)
        ]
        self.q_table = {s: [0.0]*len(ACTIONS) for s in self.state_space}
        self.last_state = None
        self.last_action = None

    def get_state(self, player, elite):  # noqa: unused
        dx = player.x - elite.x
        dy = player.y - elite.y
        return get_state_logic(dx, dy, self.bin_size)

    def choose_action(self, state):  # noqa: unused
        return choose_action_logic(state, self.q_table, ACTIONS, self.epsilon)

    def update_q(self, prev_state, action, reward, next_state):
        # Ensure states in Q-table
        if prev_state not in self.q_table:
            self.q_table[prev_state] = [0.0]*len(ACTIONS)
        if next_state not in self.q_table:
            self.q_table[next_state] = [0.0]*len(ACTIONS)
        a_idx = ACTIONS.index(action)
        update_q_logic(
            self.q_table[prev_state],
            a_idx,
            reward,
            self.q_table[next_state],
            self.alpha,
            self.gamma,
        )

    def update(self, state, map, entities, spells_system=None, explosions_system=None):  # noqa: complex
        m = self.model
        if not m.alive:
            return
        player = entities.player
        # 1. Obtener estado y elegir acción
        dx = player.x - m.x
        dy = player.y - m.y
        curr_state = get_state_logic(dx, dy, self.bin_size)
        action = choose_action_logic(curr_state, self.q_table, ACTIONS, self.epsilon)
        # 3. Ejecutar acción
        reward = 0
        if action == 'attack':
            dist_sq = dx*dx + dy*dy
            if dist_sq < 300*300:
                # Lanzar fireball hacia el jugador
                if spells_system is not None and explosions_system is not None:
                    angle = math.degrees(math.atan2(player.y - m.y, player.x - m.x))
                    # Usar tiles sólidos para colisión
                    tiles = map.solid_tiles
                    # Excluir al Elite de la lista de enemigos para que no se dañe a sí mismo
                    enemies = [e for e in entities.enemies if e is not self.model]
                    from roguelike_game.systems.combat.spells.fireball.model import FireballModel
                    from roguelike_game.systems.combat.spells.fireball.controller import FireballController
                    # Lanzar desde el centro del Elite (usando sprite_size dinámico)
                    fireball_sprite_size = 64  # Tamaño del fireball, debe coincidir con FireballModel
                    center_x = m.x + m.sprite_size[0] // 2 - fireball_sprite_size // 2
                    center_y = m.y + m.sprite_size[1] // 2 - fireball_sprite_size // 2
                    fireball_model = FireballModel(center_x, center_y, angle)
                    fireball_ctrl = FireballController(fireball_model, tiles, enemies, explosions_system)
                    spells_system.fireball_controllers.append(fireball_ctrl)
                    # Crear y agregar la vista para que el fireball sea visible
                    from roguelike_game.systems.combat.spells.fireball.view import FireballView
                    fireball_view = FireballView(fireball_model)
                    spells_system.fireball_views.append(fireball_view)
                reward = 1
            else:
                reward = -0.2
        elif action == 'dodge':
            # Buscar el proyectil más cercano (fireball)
            nearest_proj = None
            min_dist = float('inf')
            if spells_system is not None:
                for ctrl in spells_system.fireball_controllers:
                    proj = ctrl.model
                    if proj is not None and proj.alive:
                        odx = proj.x - m.x
                        ody = proj.y - m.y
                        d_sq = odx*odx + ody*ody
                        if d_sq < min_dist:
                            min_dist = d_sq
                            nearest_proj = proj
            if nearest_proj and min_dist < 200*200:
                # Esquivar perpendicularmente al proyectil
                odx = nearest_proj.x - m.x
                ody = nearest_proj.y - m.y
                perp_x = -(ody>0)-(ody<0)
                perp_y =  (odx>0)-(odx<0)
                self.movement.move(perp_x, perp_y, map.solid_tiles, entities.obstacles)
                reward = 0.2
            else:
                # Default dodge (como antes)
                dx = player.x - m.x
                dy = player.y - m.y
                perp_x = -(dy>0)-(dy<0)
                perp_y =  (dx>0)-(dx<0)
                self.movement.move(perp_x, perp_y, map.solid_tiles, entities.obstacles)
                reward = 0.05
        elif action == 'approach':
            if dx or dy:
                inv = 1/math.hypot(dx,dy)
                self.movement.move(dx*inv, dy*inv, map.solid_tiles, entities.obstacles)
            reward = 0.05
        elif action == 'retreat':
            rdx = m.x - player.x
            rdy = m.y - player.y
            if rdx or rdy:
                inv = 1/math.hypot(rdx,rdy)
                self.movement.move(rdx*inv, rdy*inv, map.solid_tiles, entities.obstacles)
            reward = 0.05
        # 4. Actualizar Q
        if self.last_state is not None:
            self.update_q(self.last_state, self.last_action, reward, curr_state)
        self.last_state = curr_state
        self.last_action = action

    def save_q_table(self, filepath='elite_q_table.json'):
        import json
        # Convert tuple keys to strings for JSON
        serializable_q = {str(k): v for k, v in self.q_table.items()}
        with open(filepath, 'w') as f:
            json.dump(serializable_q, f)

    def load_q_table(self, filepath='elite_q_table.json'):
        import json
        import ast
        try:
            with open(filepath, 'r') as f:
                data = json.load(f)
            # Convert keys back to tuples
            self.q_table = {ast.literal_eval(k): v for k, v in data.items()}
        except FileNotFoundError:
            self.q_table = {}