import time
from typing import Dict
from roguelike_game.ecs.components.abilities.dash_meter_component import DashMeterComponent

class DashResourceSystem:
    """
    Sistema que recarga cargas de dash de forma secuencial.

    - Si current < total, avanza `progress` en función del tiempo y `recharge_s`.
    - Al completar 1.0, suma una carga y reinicia `progress` para la siguiente
      (si aún faltan cargas), o lo resetea a 0 cuando está lleno.
    - Al revivir (jugador vuelve a estar sin DeathTimer), recarga todas.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Seguimiento simple del estado de muerte para detectar "revivir"
        self._was_dead: Dict[int, bool] = {}

    def update(self, world, *args):
        now = time.time()
        comps = world.components
        meters: Dict[int, DashMeterComponent] = comps.get('DashMeterComponent', {})
        death = comps.get('DeathTimer', {})
        players = comps.get('PlayerTagComponent', {})

        for eid, meter in list(meters.items()):
            # Revivir: si antes estaba muerto y ahora no, rellenar
            was_dead = self._was_dead.get(eid, False)
            is_dead = eid in death
            if was_dead and not is_dead:
                meter.current = meter.total
                meter.progress = 0.0
            self._was_dead[eid] = is_dead

            # Si está lleno, no avanzar
            if meter.current >= meter.total:
                meter.progress = 0.0
                meter.last_time = now
                continue

            # Avance secuencial
            if meter.last_time == 0.0:
                meter.last_time = now
            dt = max(0.0, now - meter.last_time)
            meter.last_time = now
            if meter.recharge_s > 0:
                meter.progress += dt / float(meter.recharge_s)
            # Completar cargas enteras si el frame fue largo
            while meter.progress >= 1.0 and meter.current < meter.total:
                meter.current += 1
                meter.progress -= 1.0
            # Si al final quedó lleno, limpiar progreso
            if meter.current >= meter.total:
                meter.current = meter.total
                meter.progress = 0.0
