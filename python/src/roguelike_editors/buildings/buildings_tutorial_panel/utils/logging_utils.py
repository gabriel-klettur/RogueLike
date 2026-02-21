import traceback

def short_stack(depth: int = 6) -> str:
    """Return a short formatted call stack.
    Excludes this helper's own frame.
    """
    try:
        frames = traceback.extract_stack(limit=depth + 2)[:-2]
        lines = []
        for fr in frames:
            file_tail = fr.filename.replace('\\', '/').split('/')[-1]
            lines.append(f"  at {file_tail}:{fr.lineno} in {fr.name}")
        return "Call stack:\n" + "\n".join(lines)
    except Exception:
        return "Call stack: <unavailable>"
