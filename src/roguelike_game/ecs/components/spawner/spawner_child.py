class SpawnerChild:
    """
    Tag component to mark NPCs that were spawned by a Spawner during runtime.
    Used to avoid persisting these ephemeral NPCs into the world save to prevent
    duplication on reload (NpcRespawnSystem) in addition to spawner-driven spawns.
    """
    def __init__(self, spawner_eid: int, wave_idx: int | None = None):
        self.spawner_eid = int(spawner_eid) if spawner_eid is not None else None
        self.wave_idx = int(wave_idx) if wave_idx is not None else None
