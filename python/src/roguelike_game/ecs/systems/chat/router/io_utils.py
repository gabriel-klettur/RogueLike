import json
import logging
import os
import re
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Optional

from roguelike_engine.log_config import build_log_filepath
from roguelike_engine.chat.service.memory_store import MemoryStore

logger = logging.getLogger(__name__)


@dataclass
class ChatIO:
    """Utility for chat-related IO concerns: memory, logs, localization.

    Holds shared state used by the ChatRouter system and collaborators.
    """
    root: Path | None = None
    mem_store: Optional[MemoryStore] = field(init=False, default=None)
    _session_id: str = field(init=False, default="")
    _session_dt: datetime = field(init=False)
    _log_dir: Optional[Path] = field(init=False, default=None)
    _npc_log_path: dict[int, str] = field(init=False, default_factory=dict)

    def __post_init__(self) -> None:
        try:
            self.root = (self.root or Path(__file__).resolve().parents[4])
        except Exception:
            self.root = Path('.')
        try:
            self.mem_store = MemoryStore(self.root)
        except Exception:
            self.mem_store = None
        try:
            self._session_id = os.urandom(4).hex()
            self._session_dt = datetime.now()
            self._log_dir = self.root / 'logs' / 'chat_sessions'
            os.makedirs(self._log_dir, exist_ok=True)
        except Exception:
            self._log_dir = None

    # ------------- Localization ---------------------------------------------
    def tr(self, code: str, es_text: str, en_text: str) -> str:
        return es_text if (code or 'es') == 'es' else en_text

    def lang_for(self, world: Any, npc_eid: int, state: Any | None = None) -> str:
        try:
            if state is not None:
                ui_lang = (getattr(state, 'chat_lang_preference', None) or '').strip().lower()
                if ui_lang in {'es', 'en'}:
                    return ui_lang
        except Exception:
            pass
        try:
            ms = MemoryStore(self.root)
            mem_key = self.memory_key(world, npc_eid)
            code = (ms.get_language(mem_key) or 'es').lower()
            return 'en' if code == 'en' else 'es'
        except Exception:
            return 'es'

    # ------------- Memory/session keys --------------------------------------
    def memory_key(self, world: Any, npc_eid: int) -> str:
        try:
            ident = world.components.get('Identity', {}).get(npc_eid)
            if ident is not None:
                name = str(getattr(ident, 'name', '') or '').strip().lower()
                stable_id = getattr(ident, 'id', None)
                if stable_id is None:
                    raise ValueError('no stable id')
                slug = re.sub(r"[^a-z0-9]+", "-", name)
                slug = re.sub(r"-+", "-", slug).strip('-') or 'npc'
                return f"{slug}-{int(stable_id)}"
        except Exception:
            pass
        return str(npc_eid)

    def resolve_persona_id(self, world: Any, target_eid: int, chat_comp: Any) -> Optional[str]:
        pid = getattr(chat_comp, 'persona_id', None) if chat_comp else None
        if pid:
            return pid
        try:
            ident = world.components.get('Identity', {}).get(target_eid)
            ent_key = getattr(ident, 'name', None) or getattr(ident, 'id', None)
            if not ent_key:
                return None
            ap = self.root / 'data' / 'chat' / 'assignments.json'
            with ap.open('r', encoding='utf-8') as f:
                data = json.load(f)
            node = data.get(str(ent_key)) or data.get(ent_key)
            if isinstance(node, dict):
                return node.get('persona_id')
        except Exception:
            return None
        return None

    # ------------- Logging ----------------------------------------------------
    def log_line(self, world: Any, npc_eid: int, sender: str, text: str, role: str | None = None) -> None:
        try:
            if not self._log_dir:
                return
            path_str = self._npc_log_path.get(int(npc_eid))
            if not path_str:
                role_s = str(role or 'npc')
                name_s = f"npc-{int(npc_eid)}"
                try:
                    ident = world.components.get('Identity', {}).get(npc_eid)
                    nm = getattr(ident, 'name', None) or getattr(ident, 'id', None)
                    if nm:
                        name_s = str(nm)
                except Exception:
                    pass

                def _slug(s: str) -> str:
                    try:
                        s2 = s.strip().lower().replace(' ', '_')
                        return re.sub(r"[^a-z0-9_\-]", '', s2)
                    except Exception:
                        return str(s)

                base = f"chat_session_{_slug(role_s)}_{_slug(name_s)}"
                path = build_log_filepath(base, directory=str(self._log_dir), extension='log', now_dt=self._session_dt)
                path_str = str(path)
                self._npc_log_path[int(npc_eid)] = path_str
            else:
                path = Path(path_str)
            with path.open('a', encoding='utf-8') as f:
                f.write(f"[{datetime.now().isoformat(timespec='seconds')}] {sender}: {text}\n")
        except Exception:
            pass

    # ------------- Online/offline estimation --------------------------------
    def estimate_online_status(self) -> bool:
        try:
            cfg_path = self.root / 'data' / 'config' / 'chat.json'
            prov = 'dummy'
            if cfg_path.exists():
                with cfg_path.open('r', encoding='utf-8') as f:
                    obj = json.load(f)
                    prov = str(obj.get('provider', 'dummy')).lower()
            if prov == 'dummy':
                return False
            if not os.getenv('OPENAI_API_KEY'):
                return False
            return True
        except Exception:
            return False
