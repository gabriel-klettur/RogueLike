from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any
import pygame

from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble


@dataclass
class MessageScheduler:
    """Schedules chat panel updates and speech bubbles over time."""

    scheduled: list[dict] = field(default_factory=list)

    def process(self, world: Any, state: Any) -> None:
        if not self.scheduled:
            return
        try:
            now = pygame.time.get_ticks()
        except Exception:
            return
        remain: list[dict] = []
        for item in self.scheduled:
            try:
                due = int(item.get('due', 0) or 0)
                if now < due:
                    remain.append(item)
                    continue
                typ = item.get('type')
                data = item.get('data') or {}
                if typ == 'chat':
                    sender = data.get('sender', 'NPC')
                    text = data.get('text', '')
                    target = data.get('target', None)
                    try:
                        if target is not None and hasattr(state, 'chat_add_message_for'):
                            state.chat_add_message_for(int(target), str(sender), str(text))
                        else:
                            state.chat_add_message(str(sender), str(text))
                    except Exception:
                        state.chat_add_message(str(sender), str(text))
                elif typ == 'chat_set':
                    idx = int(data.get('idx', -1))
                    sender = data.get('sender', 'NPC')
                    text = data.get('text', '')
                    target = data.get('target', None)
                    try:
                        if target is not None and hasattr(state, 'chat_history_for'):
                            hist = state.chat_history_for(int(target))
                            if 0 <= idx < len(hist):
                                hist[idx] = (str(sender), str(text))
                            else:
                                if hasattr(state, 'chat_add_message_for'):
                                    state.chat_add_message_for(int(target), str(sender), str(text))
                                else:
                                    state.chat_add_message(str(sender), str(text))
                        else:
                            if 0 <= idx < len(state.chat_messages):
                                state.chat_messages[idx] = (str(sender), str(text))
                            else:
                                state.chat_add_message(str(sender), str(text))
                    except Exception:
                        state.chat_add_message(str(sender), str(text))
                elif typ == 'chat_append_suffix':
                    idx = int(data.get('idx', -1))
                    suffix = str(data.get('suffix', ''))
                    target = data.get('target', None)
                    try:
                        if target is not None and hasattr(state, 'chat_history_for'):
                            hist = state.chat_history_for(int(target))
                            if 0 <= idx < len(hist):
                                sender, cur = hist[idx]
                                hist[idx] = (str(sender), str(cur) + suffix)
                        else:
                            if 0 <= idx < len(state.chat_messages):
                                sender, cur = state.chat_messages[idx]
                                state.chat_messages[idx] = (str(sender), str(cur) + suffix)
                    except Exception:
                        pass
                elif typ == 'bubble':
                    eid = data.get('eid')
                    text = data.get('text', '')
                    color = data.get('color') or (255, 255, 255)
                    ttl = int(data.get('ttl', 2500))
                    push_bubble(world, int(eid), str(text), color=tuple(color), ttl_ms=ttl)
            except Exception:
                continue
        self.scheduled = remain

    def schedule_reply_chunks(
        self,
        world: Any,
        state: Any,
        target_eid: int,
        text: str,
        *,
        color=(255, 235, 180),
        words_per_chunk: int = 12,
        delay_ms: int = 2000,
        ttl_ms: int = 2600,
    ) -> tuple[int, int | None]:
        try:
            words = (text or '').split()
        except Exception:
            words = [text] if text else []
        if not words:
            now = pygame.time.get_ticks()
            try:
                placeholder_idx = state.chat_add_message_for(int(target_eid), 'NPC', '…')
            except Exception:
                state.chat_add_message('NPC', '…')
                placeholder_idx = len(state.chat_messages) - 1
            self.scheduled.append({'due': now, 'type': 'chat_set', 'data': {'idx': int(placeholder_idx) if placeholder_idx is not None else -1, 'sender': 'NPC', 'text': '', 'target': int(target_eid)}})
            return now, placeholder_idx
        chunks = []
        i = 0
        n = max(1, int(words_per_chunk))
        while i < len(words):
            chunk = ' '.join(words[i:i+n])
            chunks.append(chunk)
            i += n
        now = pygame.time.get_ticks()
        last_due = now
        try:
            placeholder_idx = state.chat_add_message_for(int(target_eid), 'NPC', '…')
        except Exception:
            placeholder_idx = None
        agg = ''
        for i, chunk in enumerate(chunks):
            due = now + i * int(delay_ms)
            last_due = due
            agg = (agg + ' ' + chunk).strip()
            display = agg if (i == len(chunks) - 1) else (agg + ' …')
            self.scheduled.append({'due': due, 'type': 'chat_set', 'data': {'idx': placeholder_idx if placeholder_idx is not None else -1, 'sender': 'NPC', 'text': display, 'target': int(target_eid)}})
            self.scheduled.append({'due': due, 'type': 'bubble', 'data': {'eid': int(target_eid), 'text': chunk, 'color': tuple(color), 'ttl': int(ttl_ms)}})
        return last_due, placeholder_idx
