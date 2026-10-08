"""Dialog icon: the ribbon icon's blue ה׳ badge on its own, drawn per size at 8x and
downsampled, with the strokes snapped to the target pixel grid."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

BLUE = (0x18, 0x5A, 0xBD, 255)
WHITE = (255, 255, 255, 255)
SS = 8
out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)

def frame(n):
    S = n * SS
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([0, 0, S - 1, S - 1], fill=BLUE)
    k = S / 16.0                              # the design is on a 16-pixel grid
    box = lambda x0, y0, x1, y1: d.rectangle([x0 * k, y0 * k, x1 * k - 1, y1 * k - 1], fill=WHITE)
    # heh (RTL, so the detached leg is on the left): top bar, right leg, left leg
    box(6, 4, 13, 6)
    box(11, 4, 13, 12)
    box(6, 8, 8, 12)
    # geresh: a slanted tick after the letter, i.e. to its left
    d.polygon([(3.5 * k, 4 * k), (5.5 * k, 4 * k), (4.5 * k, 7.5 * k), (2.5 * k, 7.5 * k)], fill=WHITE)
    return im.resize((n, n), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 256]
frames = [frame(n) for n in sizes]
for n, f in zip(sizes, frames):
    f.save(out / f"dlg_{n}.png")
    pv = Image.new("RGBA", (n * 8, n * 8), (243, 243, 243, 255))
    pv.alpha_composite(f.resize((n * 8, n * 8), Image.NEAREST))
    pv.save(out / f"pv_dlg_{n}.png")
frames[-1].save(out / "Settings.ico", sizes=[(n, n) for n in sizes],
                append_images=frames[:-1], bitmap_format="bmp")
print("ok")
