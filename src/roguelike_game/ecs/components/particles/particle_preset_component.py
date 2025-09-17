class ParticlePresetComponent:
    """Marker component for persisted particle instances using a preset id.

    Also stores the persisted JSON entry id to keep a stable link for
    selection, dragging, and deletion.
    """
    def __init__(self, preset_id: str, entry_id: int | None = None):
        self.preset_id = str(preset_id)
        self.entry_id = int(entry_id) if entry_id is not None else None
