"""
The ten talent icons of the "Barra de Guerra" branch, as pixel art.

WHY GENERATED. The talents board bakes an icon down to its 24x24-texel socket interior
(SkillNodeView.ApplyIcon -> HudTextureBaker, a chain of bilinear halvings). Art painted at
24x24 and upscaled x8 with NEAREST lands exactly on that grid at 2 screen pixels a texel:
192 -> 96 -> 48, each halving an exact 2x2 average of identical pixels, so every texel stays
crisp. A 1024 px illustration would be averaged into mush at this size.

THE LANGUAGE, one idea per tree:
  Columnas de guerra I-V  a wide stone slab of FIVE vertical sockets; rank N lights N of them
                          in ember gold, left to right, with chevrons pushing outward to the
                          sides: "wider".
  Filas de guerra I-V     a tall slab of FIVE horizontal sockets; rank N lights N of them in
                          arcane azure, bottom to top, with chevrons pushing up and down:
                          "taller".
The lit count IS the rank, so the board reads as a progress bar without a numeral, and the two
hues keep the two trees apart at a glance. Light comes from the top left, like every other
bevel on the HUD stone.

Usage:
    python tools/atlas/icons/build_war_bar_icons.py            # write the PNGs + contact sheet
    python tools/atlas/icons/build_war_bar_icons.py --preview  # contact sheet only
"""
import sys
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "unity/Valkur/Assets/_Project/Art/Misc/talents/war_bar"
PREVIEW = ROOT / "tools/atlas/icons/generated/war_bar_icons_contact.png"
SIZE = 24
UPSCALE = 8

T = (0, 0, 0, 0)

# Stone, shared by both trees.
OUTLINE   = (14, 12, 18, 255)
STONE_HI  = (118, 110, 128, 255)
STONE     = (78, 72, 88, 255)
STONE_MID = (62, 57, 71, 255)
STONE_LO  = (40, 36, 48, 255)
RECESS    = (22, 19, 27, 255)
RECESS_LO = (48, 44, 56, 255)   # the lit lip at the bottom of an unlit socket
RECESS_HI = (10, 8, 13, 255)    # the shadow a socket's top edge casts into itself

GOLD = {"core": (255, 244, 196, 255), "hi": (255, 210, 110, 255),
        "mid": (236, 150, 48, 255), "lo": (150, 78, 22, 255),
        "glow": (255, 170, 60, 70), "arrow": (255, 200, 90, 255), "arrow_lo": (170, 96, 30, 255),
        "seam": (74, 40, 20, 255), "rivet": (255, 214, 120, 255), "aura": (255, 150, 40)}
AZURE = {"core": (220, 250, 255, 255), "hi": (140, 222, 255, 255),
         "mid": (64, 162, 232, 255), "lo": (30, 84, 150, 255),
         "glow": (90, 190, 255, 70), "arrow": (150, 225, 255, 255), "arrow_lo": (40, 110, 180, 255),
         "seam": (22, 44, 78, 255), "rivet": (170, 230, 255, 255), "aura": (70, 170, 255)}


def blend(dst, src):
    """Alpha-over of one RGBA over another."""
    sa = src[3] / 255.0
    if sa <= 0:
        return dst
    da = dst[3] / 255.0
    oa = sa + da * (1 - sa)
    if oa <= 0:
        return T
    rgb = tuple(int(round((src[i] * sa + dst[i] * da * (1 - sa)) / oa)) for i in range(3))
    return rgb + (int(round(oa * 255)),)


class Canvas:
    def __init__(self):
        self.px = [[T for _ in range(SIZE)] for _ in range(SIZE)]

    def set(self, x, y, c):
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.px[y][x] = c

    def over(self, x, y, c):
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.px[y][x] = blend(self.px[y][x], c)

    def get(self, x, y):
        return self.px[y][x] if 0 <= x < SIZE and 0 <= y < SIZE else T

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def image(self):
        img = Image.new("RGBA", (SIZE, SIZE))
        img.putdata([c for row in self.px for c in row])
        return img


def slab(cv, x0, y0, x1, y1):
    """A bevelled stone slab: dark outline, lit top-left edge, shadowed bottom-right edge,
    a faint mid-tone band so the face does not read as flat plastic."""
    cv.rect(x0, y0, x1, y1, OUTLINE)
    cv.rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1, STONE)
    for x in range(x0 + 1, x1):
        cv.set(x, y0 + 1, STONE_HI)
        cv.set(x, y1 - 1, STONE_LO)
    for y in range(y0 + 1, y1):
        cv.set(x0 + 1, y, STONE_HI)
        cv.set(x1 - 1, y, STONE_LO)
    cv.set(x0 + 1, y1 - 1, STONE_MID)
    cv.set(x1 - 1, y0 + 1, STONE_MID)
    # Knock the outline's corners off: a slab of cut stone, not a box.
    for (cx, cy) in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        cv.set(cx, cy, T)


def socket(cv, x0, y0, x1, y1, lit, pal, vertical):
    """One socket. Unlit: a recess with a lit lip. Lit: a glowing bar with a white-hot core
    along its length, the far edge falling off to the deep tone."""
    if not lit:
        cv.rect(x0, y0, x1, y1, RECESS)
        for x in range(x0, x1 + 1):
            cv.set(x, y0, RECESS_HI)
        if vertical:
            cv.set(x0, y1, RECESS_LO); cv.set(x1, y1, RECESS_LO)
        else:
            for x in range(x0, x1 + 1):
                cv.set(x, y1, RECESS_LO)
        return

    cv.rect(x0, y0, x1, y1, pal["mid"])
    if vertical:
        for y in range(y0, y1 + 1):
            cv.set(x0, y, pal["hi"])
            cv.set(x1, y, pal["mid"])
        for y in range(y0 + 1, y1):
            cv.set(x0, y, pal["core"])
        cv.set(x1, y1, pal["lo"])
    else:
        for x in range(x0, x1 + 1):
            cv.set(x, y0, pal["hi"])
            cv.set(x, y1, pal["mid"])
        for x in range(x0 + 1, x1):
            cv.set(x, y0, pal["core"])
        cv.set(x1, y1, pal["lo"])


def glow_around(cv, cells, pal, seams):
    """A one-texel halo of light on the stone around the lit sockets, so they read as LIGHT
    rather than paint — and a dark warm SEAM on the stone between two lit neighbours, so five
    lit sockets stay five and never melt into one bar of colour (the first draft did)."""
    lit = set(cells)
    for s in seams:
        cv.set(s[0], s[1], pal["seam"])
    halo = (pal["glow"][0], pal["glow"][1], pal["glow"][2], 46)
    for (x, y) in cells:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (x + dx, y + dy)
                if n in lit or n in seams:
                    continue
                c = cv.get(*n)
                if c[3] == 0 or c == OUTLINE:
                    continue
                cv.over(n[0], n[1], halo)


def aura(cv, cx, cy, radius, pal, strength):
    """A soft light behind the slab, BEFORE it is drawn: brighter with every rank, so the tree
    reads as gathering power from I to V."""
    for y in range(SIZE):
        for x in range(SIZE):
            d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
            if d >= radius:
                continue
            a = int(strength * (1 - d / radius) ** 2)
            if a > 0:
                cv.over(x, y, pal["aura"] + (a,))


def rivets(cv, x0, y0, x1, y1, pal):
    """Four rivets in the slab's corners — the HUD stone's own ornament, in the tree's hue."""
    for (x, y) in ((x0 + 1, y0 + 1), (x1 - 1, y0 + 1), (x0 + 1, y1 - 1), (x1 - 1, y1 - 1)):
        cv.set(x, y, pal["rivet"])


def sparkles(cv, points, pal):
    """The capstone's flourish: four-point stars, white core and a hued cross."""
    for (x, y) in points:
        cv.over(x, y, pal["core"])
        for (ox, oy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            cv.over(x + ox, y + oy, pal["hi"][:3] + (170,))


def chevron(cv, tip_x, tip_y, direction, pal):
    """A 3-texel chevron with a dark outline, pointing along `direction`."""
    dx, dy = direction
    pts = []
    if dx != 0:
        # <  or  >
        pts = [(tip_x, tip_y), (tip_x - dx, tip_y - 1), (tip_x - dx, tip_y + 1),
               (tip_x - 2 * dx, tip_y - 2), (tip_x - 2 * dx, tip_y + 2)]
    else:
        pts = [(tip_x, tip_y), (tip_x - 1, tip_y - dy), (tip_x + 1, tip_y - dy),
               (tip_x - 2, tip_y - 2 * dy), (tip_x + 2, tip_y - 2 * dy)]
    for (x, y) in pts:
        for ox in (-1, 0, 1):
            for oy in (-1, 0, 1):
                if cv.get(x + ox, y + oy)[3] == 0:
                    cv.set(x + ox, y + oy, OUTLINE)
    for i, (x, y) in enumerate(pts):
        cv.set(x, y, pal["arrow"] if i < 3 else pal["arrow_lo"])


def columns_icon(rank):
    cv = Canvas()
    aura(cv, 11.5, 11.5, 12.5, GOLD, 30 + 14 * rank)
    slab(cv, 3, 6, 20, 17)
    lit_cells, seams = [], []
    for i in range(5):
        x0 = 5 + i * 3
        lit = i < rank
        socket(cv, x0, 8, x0 + 1, 15, lit, GOLD, vertical=True)
        if lit:
            lit_cells += [(x0, y) for y in range(8, 16)] + [(x0 + 1, y) for y in range(8, 16)]
            if i + 1 < rank:
                seams += [(x0 + 2, y) for y in range(8, 16)]
    glow_around(cv, lit_cells, GOLD, set(seams))
    rivets(cv, 3, 6, 20, 17, GOLD)
    chevron(cv, 0, 11, (-1, 0), GOLD)
    chevron(cv, 23, 11, (1, 0), GOLD)
    if rank == 5:
        sparkles(cv, [(5, 2), (18, 3), (12, 21)], GOLD)
    return cv.image()


def rows_icon(rank):
    cv = Canvas()
    aura(cv, 11.5, 11.5, 12.5, AZURE, 30 + 14 * rank)
    slab(cv, 5, 3, 18, 20)
    lit_cells, seams = [], []
    for i in range(5):
        y1 = 18 - i * 3
        y0 = y1 - 1
        lit = i < rank
        socket(cv, 7, y0, 16, y1, lit, AZURE, vertical=False)
        if lit:
            lit_cells += [(x, y) for x in range(7, 17) for y in (y0, y1)]
            if i + 1 < rank:
                seams += [(x, y0 - 1) for x in range(7, 17)]
    glow_around(cv, lit_cells, AZURE, set(seams))
    rivets(cv, 5, 3, 18, 20, AZURE)
    chevron(cv, 11, 0, (0, -1), AZURE)
    chevron(cv, 12, 23, (0, 1), AZURE)
    if rank == 5:
        sparkles(cv, [(2, 5), (21, 10), (3, 18)], AZURE)
    return cv.image()


def main():
    icons = {}
    for r in range(1, 6):
        icons[f"war_bar_columns_{r}"] = columns_icon(r)
        icons[f"war_bar_rows_{r}"] = rows_icon(r)

    # Contact sheet: both rows of five, x10, on the board's own stone colour.
    cell = SIZE * 10
    sheet = Image.new("RGBA", (cell * 5 + 12 * 6, cell * 2 + 12 * 3), (28, 28, 36, 255))
    for r in range(1, 6):
        for row, key in enumerate(("war_bar_columns", "war_bar_rows")):
            big = icons[f"{key}_{r}"].resize((cell, cell), Image.NEAREST)
            sheet.alpha_composite(big, (12 + (r - 1) * (cell + 12), 12 + row * (cell + 12)))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(PREVIEW)
    print(f"contact sheet: {PREVIEW}")

    if "--preview" in sys.argv:
        return 0

    OUT.mkdir(parents=True, exist_ok=True)
    for name, img in icons.items():
        img.resize((SIZE * UPSCALE, SIZE * UPSCALE), Image.NEAREST).save(OUT / f"{name}.png")
    print(f"wrote {len(icons)} icons to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
