__all__ = [
    "FsmGraphPanelModel",
    "FsmGraphPanelView",
    "FsmGraphPanelController",
    "FsmGraphPanelEventHandler",
]

# Backward-compatible re-exports from the legacy monolithic modules
from .fsm_graph_panel_model import FsmGraphPanelModel  # noqa: E402,F401
from .fsm_graph_panel_view import FsmGraphPanelView  # noqa: E402,F401
from .fsm_graph_panel_controller import FsmGraphPanelController  # noqa: E402,F401
from .fsm_graph_panel_events import FsmGraphPanelEventHandler  # noqa: E402,F401

# Forward-compatible subpackages (model, view, controller, events, services)
# These allow importers to gradually adopt the modular structure, e.g.:
# from roguelike_editors.fsm.fsm_graph_panel.model import camera, selection
from . import model as model  # noqa: F401
from . import events as events  # noqa: F401
from . import controller as controller  # noqa: F401
from . import services as services  # noqa: F401
