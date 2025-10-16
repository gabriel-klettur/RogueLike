import types

from roguelike_game.ecs.systems.chat.router import system as router_system


def test_chat_events_no_target_adds_npc_message(monkeypatch):
    # Neutralizar UI y efectos
    monkeypatch.setattr(router_system, 'push_bubble', lambda *a, **k: None, raising=True)

    class FakeWorker:
        _inst = None
        def __init__(self):
            self.submitted = []
        @classmethod
        def instance(cls):
            if cls._inst is None:
                cls._inst = FakeWorker()
            return cls._inst
        def submit(self, job):
            self.submitted.append(job)
            return 'job-1'
        def poll_completed(self, max_items=8):
            return []

    class FakeIO:
        def __init__(self, root):
            self.root = root
            self.mem_store = None
        def resolve_persona_id(self, *a, **k):
            return None
        def lang_for(self, *a, **k):
            return 'es'
        def tr(self, lang, es, en):
            return es if lang == 'es' else en
        def log_line(self, *a, **k):
            pass
        def estimate_online_status(self):
            return False
        def memory_key(self, *a, **k):
            return 'npc:none'

    class FakeScheduler:
        def process(self, *a, **k):
            pass
        def schedule_reply_chunks(self, *a, **k):
            return None, None

    class FakeVendor:
        def __init__(self, io):
            pass
        def is_trader(self, *a, **k):
            return False

    sys = router_system.ChatRouterSystem(perf_log=None)
    sys.worker = FakeWorker.instance()
    sys.io = FakeIO('.')
    sys.scheduler = FakeScheduler()
    sys.vendor = FakeVendor(sys.io)

    msgs = []
    class State:
        chat_open = True
        chat_messages = []
        chat_lang_preference = 'es'
        chat_typing = False
        chat_target_eid = None
        def chat_add_message(self, who, text):
            msgs.append((who, text))

    class Ctrl:
        def get_commits(self):
            return ["hola"]

    world = types.SimpleNamespace(
        state=State(),
        _chat_input_ctrl=Ctrl(),
        player_entity=1,
        components={'ChatComponent': {}},
    )

    sys.update(world)

    assert any(who == 'NPC' and isinstance(text, str) and text for who, text in msgs)
