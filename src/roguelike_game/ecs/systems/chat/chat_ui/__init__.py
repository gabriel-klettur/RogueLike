"""Chat UI package: rendering, events, and text utilities."""

from .renderer import ChatUIRenderer  # re-export for convenience
from .events import handle_chat_ui_events  # optional, primary import remains via chat_ui_system
