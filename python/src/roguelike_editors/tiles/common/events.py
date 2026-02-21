def cycle_enum(current, delta, enum_cls):
    """
    Cycle through values of an Enum class.

    :param current: current enum member
    :param delta: +1 or -1 to move forward/backward
    :param enum_cls: the Enum class to cycle through
    :return: new enum member
    """
    members = list(enum_cls)
    idx = members.index(current)
    return members[(idx + delta) % len(members)]
