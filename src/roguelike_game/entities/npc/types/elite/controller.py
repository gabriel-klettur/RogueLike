# Path: src/roguelike_game/entities/npc/types/elite/controller.py
from roguelike_game.entities.npc.types.monster.controller import MonsterController
from .model import EliteModel

import random
import numpy as np

class EliteController(MonsterController):
    def __init__(self, model: EliteModel):
        super().__init__(model)
        # Q-learning params
        self.actions = ['attack', 'dodge', 'approach', 'retreat']
        self.state_space = []  # Lista de todos los estados posibles (se poblará dinámicamente)
        self.q_table = {}  # Diccionario: (state, action) -> valor Q
        self.epsilon = 0.2  # Probabilidad de explorar
        self.alpha = 0.5    # Tasa de aprendizaje
        self.gamma = 0.9    # Factor de descuento
        self.last_state = None
        self.last_action = None

    def get_state(self, player, elite):
        """
        Define un estado simple basado en la distancia y dirección relativa al jugador.
        """
        dx = player.x - elite.x
        dy = player.y - elite.y
        dist = np.hypot(dx, dy)
        # Discretizamos la distancia y dirección
        dist_bin = int(dist // 100)  # Ej: 0=cerca, 1=medio, 2=lejos
        dir_bin = (int(np.sign(dx)), int(np.sign(dy)))  # (-1,0,1) para cada eje
        return (dist_bin, dir_bin)

    def choose_action(self, state):
        # Epsilon-greedy
        if random.random() < self.epsilon or state not in self.q_table:
            return random.choice(self.actions)
        # Elegir acción con mayor Q
        q_values = self.q_table.get(state, {a: 0 for a in self.actions})
        return max(q_values, key=q_values.get)

    def update_q(self, prev_state, action, reward, next_state):
        if prev_state not in self.q_table:
            self.q_table[prev_state] = {a: 0 for a in self.actions}
        if next_state not in self.q_table:
            self.q_table[next_state] = {a: 0 for a in self.actions}
        max_next = max(self.q_table[next_state].values())
        old_q = self.q_table[prev_state][action]
        self.q_table[prev_state][action] = old_q + self.alpha * (reward + self.gamma * max_next - old_q)

    def update(self, state, map, entities, spells_system=None, explosions_system=None):
        m = self.model
        if not m.alive:
            return
        player = entities.player
        # 1. Obtener estado actual
        curr_state = self.get_state(player, m)
        # 2. Elegir acción
        action = self.choose_action(curr_state)
        # 3. Ejecutar acción
        reward = 0
        if action == 'attack':
            dist = np.hypot(player.x - m.x, player.y - m.y)
            if dist < 300:
                # Lanzar fireball hacia el jugador
                if spells_system is not None and explosions_system is not None:
                    angle = np.degrees(np.arctan2(player.y - m.y, player.x - m.x))
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
                for ctrl in getattr(spells_system, 'fireball_controllers', []):
                    proj = getattr(ctrl, 'model', None)
                    if proj is not None and proj.alive:
                        d = np.hypot(proj.x - m.x, proj.y - m.y)
                        if d < min_dist:
                            min_dist = d
                            nearest_proj = proj
            if nearest_proj and min_dist < 200:
                # Esquivar perpendicularmente al proyectil
                dx = nearest_proj.x - m.x
                dy = nearest_proj.y - m.y
                perp = (-np.sign(dy), np.sign(dx))
                self.movement.move(perp[0], perp[1], map.solid_tiles, entities.obstacles)
                reward = 0.2
            else:
                # Default dodge (como antes)
                dx = player.x - m.x
                dy = player.y - m.y
                perp = (-np.sign(dy), np.sign(dx))
                self.movement.move(perp[0], perp[1], map.solid_tiles, entities.obstacles)
                reward = 0.05
        elif action == 'approach':
            dx = player.x - m.x
            dy = player.y - m.y
            norm = np.hypot(dx, dy)
            if norm > 0:
                self.movement.move(dx / norm, dy / norm, map.solid_tiles, entities.obstacles)
            reward = 0.05
        elif action == 'retreat':
            dx = m.x - player.x
            dy = m.y - player.y
            norm = np.hypot(dx, dy)
            if norm > 0:
                self.movement.move(dx / norm, dy / norm, map.solid_tiles, entities.obstacles)
            reward = 0.05
        # 4. Actualizar Q
        if self.last_state is not None and self.last_action is not None:
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