from network.client import NetworkClient
from entities.remote_player.base import RemotePlayer

def init_multiplayer(state):
    state.network = NetworkClient(state.player)
    state.network.start()
    state.is_multiplayer = True

def render_remote_players(state):
    if hasattr(state, "network") and state.network.connected:
        for pid, data in state.network.remote_players.items():
            if pid != state.player.character_name:
                rp = RemotePlayer(data["x"], data["y"])
                rp.render(state.screen, state.camera)