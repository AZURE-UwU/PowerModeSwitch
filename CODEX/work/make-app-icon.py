# @file make-app-icon.py
# @author Codex
# @module PowerModeSwitch.Assets

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "CODEX" / "GeneratedAssets"
PREVIEW = ASSETS / "app-icon.png"
ICON = ASSETS / "app.ico"

def create_gradient(size: int) -> Image.Image:
    start = np.array((55, 106, 176), dtype=np.float32)
    end = np.array((21, 60, 112), dtype=np.float32)
    y, x = np.mgrid[0:size, 0:size]
    ratio = np.clip((x + y) / ((size - 1) * 2), 0.0, 1.0)[..., None]
    pixels = start + (end - start) * ratio
    rgba = np.empty((size, size, 4), dtype=np.uint8)
    rgba[..., :3] = pixels.astype(np.uint8)
    rgba[..., 3] = 255
    return Image.fromarray(rgba, mode="RGBA")


def create_icon(size: int) -> Image.Image:
    render_size = size * 4
    canvas = Image.new("RGBA", (render_size, render_size), (0, 0, 0, 0))
    mask = Image.new("L", (render_size, render_size), 0)
    padding = render_size * 0.045
    ImageDraw.Draw(mask).rounded_rectangle(
        (padding, padding, render_size - padding, render_size - padding),
        radius=render_size * 0.22,
        fill=255,
    )

    background = create_gradient(render_size)
    canvas.alpha_composite(
        Image.composite(background, Image.new("RGBA", canvas.size), mask)
    )

    draw = ImageDraw.Draw(canvas)
    compact = size < 32
    font = ImageFont.truetype(
        r"C:\Windows\Fonts\segoeuib.ttf",
        size=round(render_size * (0.60 if compact else 0.47)),
    )
    draw.text(
        (
            render_size * 0.5,
            render_size * (0.52 if compact else 0.37),
        ),
        "P",
        font=font,
        fill=(255, 255, 255, 255),
        anchor="mm",
    )

    if not compact:
        bolt = [
            (render_size * 0.542, render_size * 0.610),
            (render_size * 0.361, render_size * 0.786),
            (render_size * 0.459, render_size * 0.786),
            (render_size * 0.400, render_size * 0.913),
            (render_size * 0.630, render_size * 0.728),
            (render_size * 0.522, render_size * 0.728),
            (render_size * 0.581, render_size * 0.610),
        ]
        draw.polygon(bolt, fill=(255, 255, 255, 255))

    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    create_icon(1024).save(PREVIEW, format="PNG")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [create_icon(size) for size in sizes]
    frames[-1].save(
        ICON,
        format="ICO",
        sizes=[(size, size) for size in sizes],
        append_images=frames[:-1],
    )


if __name__ == "__main__":
    main()
