import pygame
from typing import Optional, Tuple
from roguelike_game.ecs.components.chat.floating_chat_bubble import FloatingChatBubbleComponent, ChatBubble


def push_bubble(world, eid: int, text: str, color: Optional[Tuple[int, int, int]] = None, ttl_ms: int = 2500) -> None:
    """
    Añade una burbuja de texto a la entidad dada. Crea el componente si no existe.
    """
    if not world or eid is None:
        return
    comps = getattr(world, 'components', None)
    if comps is None:
        return
    fmap = comps.setdefault('FloatingChatBubbleComponent', {})
    comp: FloatingChatBubbleComponent = fmap.get(eid)
    if comp is None:
        comp = FloatingChatBubbleComponent()
        fmap[eid] = comp
    now = pygame.time.get_ticks()
    bubble = ChatBubble(text=str(text), created_ms=int(now), ttl_ms=int(ttl_ms), color=tuple(color) if color else (255, 255, 255))
    comp.bubbles.append(bubble)
