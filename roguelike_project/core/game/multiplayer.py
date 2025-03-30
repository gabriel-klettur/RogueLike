# core/game/multiplayer.py

from entities.remote_player.base import RemotePlayer

def render_remote_players(state):
    if hasattr(state, "websocket") and state.websocket and state.websocket_connected:
        for pid, data in state.websocket.remote_players.items():
            if pid != state.websocket.id:
                rp = RemotePlayer(
                    x=data.get("x", 0),
                    y=data.get("y", 0),
                    pid=pid,
                    character=data.get("character", "first_hero"),
                    direction=data.get("direction", "down"),
                    health=data.get("health", 100),
                    mana=data.get("mana", 50),
                    energy=data.get("energy", 100)
                )
                rp.render(state.screen, state.camera)
