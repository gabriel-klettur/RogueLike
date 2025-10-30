class BoomerangGlowTag:
    def __init__(self, owner_eid: int, particle_eid: int | None = None):
        self.owner_eid = int(owner_eid)
        self.particle_eid = particle_eid if isinstance(particle_eid, int) else None
