from entities.remote_player.base import RemotePlayer

def render_remote_players(state):
    if hasattr(state, "websocket") and state.websocket and state.websocket_connected:
        for pid, data in state.websocket.remote_players.items():
            if pid != state.websocket.id:
                rp = RemotePlayer(data["x"], data["y"], pid)
                rp.render(state.screen, state.camera)