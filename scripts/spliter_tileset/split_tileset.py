#!/usr/bin/env python3
import os
import argparse
from PIL import Image, ImageDraw, ImageFont

def slice_tileset(image_path: str, cols: int, rows: int, output_prefix: str):
    """
    Divide la imagen en cols×rows y guarda cada tile individualmente.
    """
    img = Image.open(image_path)
    w, h = img.size
    tile_w = w // cols
    tile_h = h // rows

    out_dir = f"{output_prefix}_tiles"
    os.makedirs(out_dir, exist_ok=True)

    count = 1
    for r in range(rows):
        for c in range(cols):
            left, upper = c * tile_w, r * tile_h
            right, lower = left + tile_w, upper + tile_h
            tile = img.crop((left, upper, right, lower))
            tile.save(os.path.join(out_dir, f"{output_prefix}_{count}.png"))
            count += 1


def draw_grid_overlay(image_path: str, cols: int, rows: int, output_prefix: str):
    """
    Crea una copia de la imagen con líneas de grid y números de índice.
    """
    img = Image.open(image_path).convert("RGBA")
    w, h = img.size
    tile_w = w // cols
    tile_h = h // rows

    # Capa transparente para dibujar
    overlay = Image.new("RGBA", img.size)
    draw = ImageDraw.Draw(overlay)

    # Opciones de dibujo
    line_color = (255, 0, 0, 200)  # rojo semitransparente
    text_color = (255, 255, 255, 255)  # blanco
    # Fuente: intentamos Arial o la fuente por defecto
    try:
        font = ImageFont.truetype("arial.ttf", min(tile_w, tile_h) // 4)
    except IOError:
        font = ImageFont.load_default()

    # Dibujar líneas verticales y horizontales
    for i in range(1, cols):
        x = i * tile_w
        draw.line([(x, 0), (x, h)], fill=line_color, width=2)
    for j in range(1, rows):
        y = j * tile_h
        draw.line([(0, y), (w, y)], fill=line_color, width=2)

    # Dibujar números en el centro de cada tile
    index = 1
    for r in range(rows):
        for c in range(cols):
            x0 = c * tile_w
            y0 = r * tile_h
            text = str(index)
            # Medir tamaño del texto
            try:
                text_w, text_h = font.getsize(text)
            except AttributeError:
                bbox = draw.textbbox((0, 0), text, font=font)
                text_w, text_h = bbox[2] - bbox[0], bbox[3] - bbox[1]
            # Posición centrada
            tx = x0 + (tile_w - text_w) / 2
            ty = y0 + (tile_h - text_h) / 2
            draw.text((tx, ty), text, font=font, fill=text_color)
            index += 1

    # Combinar imagen original y overlay
    result = Image.alpha_composite(img, overlay)
    out_path = f"{output_prefix}_grid.png"
    result.convert("RGB").save(out_path)
    print(f"Grid overlay saved as: {out_path}")


def main():
    parser = argparse.ArgumentParser(
        description="Divide un tileset en NxM y crea un overlay con grid e índices"
    )
    parser.add_argument("image_path",
                        help="Ruta al archivo de imagen (p.ej. tileset.png)")
    parser.add_argument("cols", type=int,
                        help="Número de columnas en el tileset")
    parser.add_argument("rows", type=int,
                        help="Número de filas en el tileset")
    args = parser.parse_args()

    base = os.path.splitext(os.path.basename(args.image_path))[0]
    slice_tileset(args.image_path, args.cols, args.rows, base)
    draw_grid_overlay(args.image_path, args.cols, args.rows, base)

    total = args.cols * args.rows
    print(f"¡Hecho! Se han generado {total} imágenes en '{base}_tiles/' y el grid en '{base}_grid.png'.")

if __name__ == "__main__":
    main()
