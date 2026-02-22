import time
import os
import json
from roguelike_game.ecs.components.abilities.combo_counter_component import ComboCounterComponent


class ComboSystem:
    """
    Sistema de actualización del contador de combos.

    - Consume eventos en world.components['ComboEventQueue'] con campos:
      { 'attacker': eid, 'target': eid, 'damage': float, 'source': str, 'time': float }
    - Incrementa el contador del atacante (si tiene ComboCounterComponent) cuando pasa filtros básicos.
    - Resetea el combo cuando la ventana expira.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Hot-reload de reglas desde JSON
        self._rules_path = os.path.join(os.getcwd(), 'data', 'config', 'combo_rules.json')
        self._last_mtime = None
        self._last_load_t = 0.0
        self._reload_interval_s = 1.0

    def _maybe_reload_rules(self, world):
        now = time.time()
        if (now - self._last_load_t) < self._reload_interval_s:
            return
        self._last_load_t = now
        try:
            mtime = os.path.getmtime(self._rules_path)
        except Exception:
            return
        if self._last_mtime is not None and mtime == self._last_mtime:
            return
        # Cargar y aplicar
        try:
            with open(self._rules_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
        except Exception:
            return
        self._last_mtime = mtime
        # Aplicar al jugador (si existe sección 'player')
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        p = data.get('player') or {}
        # Ajustar ventana y cooldown de objetivo igual
        cnt_map = world.components.get('ComboCounterComponent', {})
        cnt = cnt_map.get(player_eid)
        if cnt is not None:
            try:
                if 'window_s' in p:
                    cnt.window_s = float(p.get('window_s', cnt.window_s))
                if 'min_window_s' in p:
                    cnt.min_window_s = float(p.get('min_window_s', cnt.min_window_s))
                if 'difficulty_increase_per_hit' in p:
                    cnt.difficulty_increase_per_hit = float(p.get('difficulty_increase_per_hit', cnt.difficulty_increase_per_hit))
                if 'break_flash_duration_s' in p:
                    cnt.break_flash_duration_s = float(p.get('break_flash_duration_s', cnt.break_flash_duration_s))
                if 'same_target_cooldown_s' in p:
                    cnt.same_target_cooldown_s = float(p.get('same_target_cooldown_s', cnt.same_target_cooldown_s))
            except Exception:
                pass
        # Reglas
        rules_in = (p.get('rules') or {}) if isinstance(p, dict) else {}
        rules_map = world.components.get('ComboRulesComponent', {})
        rules = rules_map.get(player_eid)
        if rules is not None and isinstance(rules_in, dict):
            try:
                allowed = rules_in.get('allowed_sources')
                if isinstance(allowed, dict):
                    rules.allowed_sources = allowed
                if 'min_damage' in rules_in:
                    rules.min_damage = float(rules_in.get('min_damage', rules.min_damage))
                if 'require_enemy' in rules_in:
                    rules.require_enemy = bool(rules_in.get('require_enemy', rules.require_enemy))
                if 'require_unique_target' in rules_in:
                    rules.require_unique_target = bool(rules_in.get('require_unique_target', rules.require_unique_target))
            except Exception:
                pass

    def update(self, world, camera=None):
        comps = world.components
        # 0) Hot reload de reglas
        self._maybe_reload_rules(world)
        queue = comps.setdefault('ComboEventQueue', [])
        now = time.time()
        # 1) Procesar eventos de combo (dealt hits)
        if queue:
            for ev in list(queue):
                # Ruptura explícita
                if ev.get('type') == 'break' or ev.get('action') == 'break':
                    entity = ev.get('entity')
                    if entity is not None:
                        counter = comps.get('ComboCounterComponent', {}).get(entity)
                        if counter:
                            # Registrar combo completado antes de romper si había progreso
                            if counter.current > 0:
                                counter.last_completed_count = counter.current
                                counter.total_completed += 1
                            counter.reset()
                            # Marcar flash de ruptura
                            tnow = now
                            try:
                                tnow = float(ev.get('time', now))
                            except Exception:
                                tnow = now
                            counter.break_flash_end_time = tnow + float(getattr(counter, 'break_flash_duration_s', 0.3))
                    continue
                # Kill dentro de ventana activa -> incrementa contador de combos (kills)
                if ev.get('type') == 'kill':
                    attacker = ev.get('entity')
                    if attacker is not None:
                        counter = comps.get('ComboCounterComponent', {}).get(attacker)
                        if counter:
                            if counter.is_active(now):
                                counter.kill_combo_current += 1
                                if counter.kill_combo_current > counter.kill_combo_best:
                                    counter.kill_combo_best = counter.kill_combo_current
                    continue
                attacker = ev.get('attacker')
                target = ev.get('target')
                dmg = float(ev.get('damage', 0))
                t = float(ev.get('time', now))
                source = ev.get('source') or 'unknown'
                if attacker is None or target is None:
                    continue
                # Requiere componente de combo en el atacante
                counter_map = comps.setdefault('ComboCounterComponent', {})
                counter: ComboCounterComponent = counter_map.get(attacker)
                if not counter:
                    continue
                # Filtrado básico: ignorar daño <= 0
                if dmg <= 0:
                    continue
                # Reglas opcionales
                rules_map = comps.get('ComboRulesComponent', {})
                rules = rules_map.get(attacker)
                if rules is not None:
                    # fuente permitida
                    try:
                        if not bool(rules.allowed_sources.get(source, False)):
                            # Aun así refrescar ventana si está activa
                            if counter.is_active(t):
                                eff = counter._effective_window_for_count(counter.current if counter.current > 0 else 1)
                                counter.window_end_time = t + eff
                            continue
                    except Exception:
                        pass
                    # daño mínimo
                    try:
                        if float(dmg) < float(getattr(rules, 'min_damage', 0.0)):
                            if counter.is_active(t):
                                eff = counter._effective_window_for_count(counter.current if counter.current > 0 else 1)
                                counter.window_end_time = t + eff
                            continue
                    except Exception:
                        pass
                    # solo enemigos (evitar contar si el target es el jugador)
                    if getattr(rules, 'require_enemy', True):
                        if target in comps.get('PlayerTagComponent', {}):
                            if counter.is_active(t):
                                eff = counter._effective_window_for_count(counter.current if counter.current > 0 else 1)
                                counter.window_end_time = t + eff
                            continue
                    # objetivo distinto (si se exige alternar)
                    if getattr(rules, 'require_unique_target', False):
                        if counter.last_target_id is not None and counter.last_target_id == target:
                            if counter.is_active(t):
                                eff = counter._effective_window_for_count(counter.current if counter.current > 0 else 1)
                                counter.window_end_time = t + eff
                            continue
                # Anti-spam: evitar contar repetidamente el mismo target dentro del cooldown
                last_t = counter.last_hit_time_by_target.get(target)
                if last_t is not None and (t - last_t) < float(counter.same_target_cooldown_s):
                    # Aún así refrescar ventana si ya había combo activo
                    if counter.is_active(t):
                        eff = counter._effective_window_for_count(counter.current if counter.current > 0 else 1)
                        counter.window_end_time = t + eff
                    continue
                # Golpe válido -> incrementar y refrescar ventana
                counter.on_valid_hit(target, at_time=t)
            # Limpiar la cola completamente tras procesar
            queue.clear()
        # 2) Expirar combos cuya ventana haya terminado
        counter_map = comps.get('ComboCounterComponent', {})
        if counter_map:
            for eid, counter in list(counter_map.items()):
                if counter.current > 0 and now >= float(counter.window_end_time):
                    # Registrar combo completado por expiración natural
                    counter.last_completed_count = counter.current
                    counter.total_completed += 1
                    counter.reset()
