# Migración: Player a FSM

Este documento describe los pasos necesarios para migrar la entidad **Player** al bucle del `FSMSystem`, de modo que utilice una máquina de estados al igual que los NPCs.

---

## 1. Crear estados de Player ✅

Carpeta: `src/roguelike_game/ecs/fsm/states/player`

- **MoveState** (`move_state.py`):
  - Hereda de `State`. Maneja input de movimiento, actualiza `Velocity` y anima “walk”.
- **PlayerAttackState** (`player_attack_state.py`):
  - Hereda de `AttackState` o `State`. Gestiona ataques cuerpo a cuerpo.
- **PlayerSpellSelectState** (`player_spell_select_state.py`):
  - Permite al jugador seleccionar qué hechizo lanzar.
- **PlayerSpellCastState** (`player_spell_cast_state.py`):
  - Hereda de `CastState`. Inicia animación y contexto del hechizo.
- **PlayerSpellChannelState** (`player_spell_channel_state.py`):
  - Hereda de `ChannelSpellState`.
- **PlayerSpellReleaseState** (`player_spell_release_state.py`):
  - Hereda de `ReleaseSpellState`.
- **PlayerSpellCooldownState** (`player_spell_cooldown_state.py`):
  - Hereda de `CooldownState`.
- **PlayerInteractState** (`player_interact_state.py`):
  - Para interacciones (recoger objetos, puertas, etc.).

> En cada archivo: crear clase con `__init__`, métodos `on_enter`, `on_update`, `on_exit`.
> **Nota**: crea un archivo `__init__.py` en `ecs/fsm/states/player` para registrar el paquete.

---

## 2. Añadir NPCState al Player ✅

Modificar `ecs/factories/player/player_factory.py`:

```diff
@@ def spawn_player(...):
      # 11) Efecto visual: Trail de sombra
      world.components["TrailComponent"][eid] = TrailComponent(config=trail_cfg)

+    # 12) FSM del Player
+    from roguelike_game.ecs.components.fsm.npc_state import NPCState
+    from roguelike_game.ecs.fsm.states.idle_state import IdleState
+    world.components["NPCState"][eid] = NPCState(IdleState())

      return eid
```

---

## 3. Integrar InputSystem con FSM ✅

### 3.1 Agregar imports

Al inicio de `ecs/systems/input/input_system.py`, añade estas líneas:
```python
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_game.ecs.fsm.states.player.move_state import MoveState
from roguelike_game.ecs.fsm.states.player.player_attack_state import PlayerAttackState
from roguelike_game.ecs.fsm.states.player.player_spell_select_state import PlayerSpellSelectState
from roguelike_game.ecs.fsm.states.player.player_spell_cast_state import PlayerSpellCastState
```

Actualizar `ecs/systems/input/input_system.py` para entidades con `PlayerTagComponent`:

- Detectar movimiento y cambiar a `MoveState` o volver a `IdleState`.
- Detectar botón de ataque → `PlayerAttackState`.
- Detectar hechizo → `PlayerSpellSelectState` → `PlayerSpellCastState`.

Ejemplo de diff:

```diff
@@ input_system.py
-    for eid, inp in inputs.items():
+    for eid, inp in inputs.items():
         if world.has_component(eid, PlayerTagComponent):
-            # código actual de Velocity...
+            state = world.components["NPCState"][eid].fsm.current_state
+            # Movimiento
+            if inp.move and isinstance(state, IdleState):
+                world.components["NPCState"][eid].fsm.change_state(MoveState(), eid)
+            elif not inp.move and isinstance(state, MoveState):
+                world.components["NPCState"][eid].fsm.change_state(IdleState(), eid)
+            # Ataque físico
+            if inp.attack:
+                world.components["NPCState"][eid].fsm.change_state(PlayerAttackState(), eid)
+            # Hechizos
+            if inp.spell:
+                world.components["NPCState"][eid].fsm.change_state(PlayerSpellSelectState(), eid)
```

---

## 4. Revisar FSMSystem ✅

No son necesarios cambios: `FSMSystem` ya actualiza todas las entidades con `NPCState`.

---

## 5. Pruebas

1. Iniciar juego y activar debug de estados para el Player.
2. Verificar transición **Idle → Move → Idle**.
3. Probar ataque físico (debe bloquear movimiento durante el estado).
4. Probar casting completo de hechizos: **Select → Cast → Channel → Release → Cooldown**.

---

Al completar estos pasos, tu **Player** formará parte del mismo loop `FSMSystem` que los NPCs, aprovechando todas las ventajas de la máquina de estados.