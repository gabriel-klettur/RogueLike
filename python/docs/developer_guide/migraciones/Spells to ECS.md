A grandes rasgos ya tienes todo el “pipeline” de pixel_fire caminando en tu ECS/FSM, pero aún quedan estos puntos por revisar o migrar:

1. Añadir el import de la clase base State en cada archivo de estado
(por ejemplo, en prepare_spell_state.py, channel_spell_state.py,
release_spell_state.py, resolve_spell_state.py y
cooldown_state.py falta:
```python
from roguelike_game.ecs.fsm.state import State
```
2. Verificar que FSMSystem (en ecs/systems/fsm/fsm_system.py) recorra
y llame a fsm.update(entity, dt) sobre todas las entidades con
componente NPCState.
3. Eliminar el antiguo MVC roguelike_game/systems/combat/spells/spells_system.py
(y sus spawn_fireball, etc.), ahora obsoleto si todo va vía ECS.
4. Corregir spells_config.py (asegúrate de import json) para que no falle
al leer data/spells.json.
5. Probar el flujo entero para NPCs:
Prepare → Channel → Release → Resolve → Cooldown.
6. Decidir si quieres canalizar/tener FSM también para el jugador (hoy haces “instant
fireball” al click, pero podrías reusar la sub-FSM si quieres animación Q/E).
7. Migrar el resto de hechizos (smoke, firework, lightning, etc.) al mismo
esquema ECS/FSM: crear sus componentes, estados (Prepare…, Release…, etc.),
añadirlos a SpellCastingSystem o crear sistemas análogos y poblarlos desde InputSystem.
Con eso tendrías la migración completa de tu sistema de hechizos al ECS/FSM.