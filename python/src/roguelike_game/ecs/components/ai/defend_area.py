from dataclasses import dataclass

@dataclass
class DefendArea:
    """
    Defines a defend area in pixels for an NPC.

    Shape-aware:
    - shape: "circle" (default) or "square".
      - circle: radius_px is the circular radius.
      - square: radius_px is the half-side (half extent). The full side is 2*radius_px.
    - center_x, center_y are pixel coordinates of the area's center.
    - leash controls whether the NPC should be leashed back inside the area.
    """
    center_x: float
    center_y: float
    radius_px: float
    leash: bool = True
    shape: str = "circle"
