import pygame


def format_key_label(keyname: str) -> str:
    """Return a user-facing label for an internal key name.
    - Accepts strings like 'K_*' (pygame constant names) or plain names.
    - Hides the 'K_' prefix.
    - Uses pygame.key.name() for canonical naming when possible.
    - Maps arrow keys to symbols: ↑ ↓ ← →
    - Applies small normalizations (Esc, Enter, title casing, single-letter upper).
    """
    if not keyname:
        return "—"
    if keyname == "—":
        return keyname

    # Mouse buttons mapping (string-based)
    if isinstance(keyname, str) and keyname.startswith('M_'):
        m = keyname.upper()
        mouse_map = {
            'M_LEFT': 'Left Click',
            'M_MIDDLE': 'Middle Click',
            'M_RIGHT': 'Right Click',
            'M_X1': 'Mouse Button 4',
            'M_X2': 'Mouse Button 5',
        }
        return mouse_map.get(m, m.replace('M_', 'Mouse ').title())

    # Resolve using pygame if it's a keyboard constant-like name
    if isinstance(keyname, str) and keyname.startswith('K_'):
        try:
            keycode = getattr(pygame, keyname)
            label = pygame.key.name(keycode)
        except Exception:
            label = keyname[2:]
    else:
        label = str(keyname)

    if not label:
        label = str(keyname[2:] if isinstance(keyname, str) and keyname.startswith('K_') else keyname)

    # Normalize to lowercase for mapping checks
    raw = label.strip()
    low = raw.lower()

    # Map common specials to nicer labels
    special_map = {
        'escape': 'Esc',
        'return': 'Enter',
        'kp_enter': 'Enter',
        'backspace': 'Backspace',
        'tab': 'Tab',
        'space': 'Space',
        'pageup': 'Page Up',
        'pagedown': 'Page Down',
        'home': 'Home',
        'end': 'End',
        'insert': 'Insert',
        'delete': 'Delete',
        'printscreen': 'Print Screen',
        'scrolllock': 'Scroll Lock',
        'pause': 'Pause',
        'numlock': 'Num Lock',
        'capslock': 'Caps Lock',
        'f1': 'F1', 'f2': 'F2', 'f3': 'F3', 'f4': 'F4', 'f5': 'F5',
        'f6': 'F6', 'f7': 'F7', 'f8': 'F8', 'f9': 'F9', 'f10': 'F10',
        'f11': 'F11', 'f12': 'F12', 'f13': 'F13', 'f14': 'F14', 'f15': 'F15',
        # Modifiers (left/right collapsed for cleaner UI)
        'lshift': 'Shift', 'rshift': 'Shift',
        'lctrl': 'Ctrl', 'rctrl': 'Ctrl', 'lcontrol': 'Ctrl', 'rcontrol': 'Ctrl',
        'lalt': 'Alt', 'ralt': 'Alt', 'lmeta': 'Meta', 'rmeta': 'Meta',
        'lgui': 'Meta', 'rgui': 'Meta',
        # Keypad
        'kp_0': 'Numpad 0', 'kp_1': 'Numpad 1', 'kp_2': 'Numpad 2', 'kp_3': 'Numpad 3',
        'kp_4': 'Numpad 4', 'kp_5': 'Numpad 5', 'kp_6': 'Numpad 6', 'kp_7': 'Numpad 7',
        'kp_8': 'Numpad 8', 'kp_9': 'Numpad 9', 'kp_period': 'Numpad .', 'kp_divide': 'Numpad /',
        'kp_multiply': 'Numpad *', 'kp_minus': 'Numpad -', 'kp_plus': 'Numpad +',
    }

    # Arrow icons
    arrows = {
        'up': '↑',
        'down': '↓',
        'left': '←',
        'right': '→',
    }

    if low in arrows:
        return arrows[low]

    if low in special_map:
        return special_map[low]

    # Single-character keys: show uppercase
    if len(raw) == 1:
        return raw.upper()

    # Default: Title Case and trim spaces
    pretty = raw.title()
    pretty = ' '.join(pretty.split())
    return pretty
