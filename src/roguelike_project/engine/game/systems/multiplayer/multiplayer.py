from src.roguelike_game.entities.remote_player.base import RemotePlayer

def render_remote_players(state):
    if hasattr(state, "websocket") and state.websocket and state.websocket_connected:
        for pid, data in state.websocket.remote_players.items():
            if pid == state.websocket.id:
                continue

            if pid not in state.remote_entities:
                state.remote_entities[pid] = RemotePlayer(x=data["x"], y=data["y"], pid=pid, character=data["character"])

            rp = state.remote_entities[pid]
            rp.x = data.get("x", 0)
            rp.y = data.get("y", 0)
            rp.direction = data.get("direction", "down")
            rp.health = data.get("health", 100)
            rp.mana = data.get("mana", 50)
            rp.energy = data.get("energy", 100)
            rp.sprite = rp.sprites.get(rp.direction, rp.sprite)

            # ✅ Solo renderizar si está visible en cámara
            if state.camera.is_in_view(rp.x, rp.y, rp.sprite_size):
                rp.render(state.screen, state.camera)
