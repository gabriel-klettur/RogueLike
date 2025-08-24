from __future__ import annotations

from types import SimpleNamespace

from roguelike_editors.fsm.fsm_sets_panel.fsm_sets_panel_controller import FsmSetsPanelController
from roguelike_editors.fsm.fsm_sets_panel.fsm_sets_panel_model import FsmSetsPanelModel


def test_refresh_items_from_disk(monkeypatch):
    ctl = FsmSetsPanelController(model=FsmSetsPanelModel())

    # Monkeypatch persistence to return predictable sets
    import roguelike_editors.fsm.fsm_sets_panel.fsm_sets_panel_controller as mod
    monkeypatch.setattr(mod, 'default_sets_path', lambda: '__unused__', raising=True)
    monkeypatch.setattr(mod, 'load_sets', lambda path: {
        'sets': [
            {'id': 'Player_Base'},
            {'id': 'Monster_Goblin'},
        ]
    }, raising=True)

    ctl._refresh_items_from_disk()
    assert ctl.model.items == ['Player_Base', 'Monster_Goblin']


def test_handle_event_delegation(monkeypatch):
    ctl = FsmSetsPanelController(model=FsmSetsPanelModel())

    called = {'n': 0}
    def _h(self, controller, event):
        called['n'] += 1
        return True

    monkeypatch.setattr(ctl, 'events', SimpleNamespace(handle_event=lambda controller, event: _h(None, controller, event)))

    consumed = ctl.handle_event(SimpleNamespace(kind='dummy'))
    assert consumed is True
    assert called['n'] == 1
