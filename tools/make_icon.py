"""Generates Assets/guardrail.ico and Assets/guardrail.png. Run: python tools/make_icon.py"""
from PIL import Image, ImageDraw, ImageFilter
import os, sys

S = 1024
def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def bezier(p0, p1, p2, p3, n=60):
    pts = []
    for i in range(n + 1):
        t = i / n; u = 1 - t
        pts.append((u**3*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t**3*p3[0],
                    u**3*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t**3*p3[1]))
    return pts

def shield(cx, top, w, h):
    l, r, bot = cx - w/2, cx + w/2, top + h
    pts = []
    pts += bezier((cx, top), (cx + w*0.18, top + h*0.07), (r - w*0.08, top + h*0.06), (r, top + h*0.10))
    pts += bezier((r, top + h*0.10), (r + w*0.01, top + h*0.55), (cx + w*0.28, bot - h*0.12), (cx, bot))
    pts += bezier((cx, bot), (cx - w*0.28, bot - h*0.12), (l - w*0.01, top + h*0.55), (l, top + h*0.10))
    pts += bezier((l, top + h*0.10), (l + w*0.08, top + h*0.06), (cx - w*0.18, top + h*0.07), (cx, top))
    return pts

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Rounded-square background with vertical blue gradient
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    gd.line([(0, y), (S, y)], fill=lerp((47, 132, 200, 255), (20, 60, 96, 255), y / S))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=210, fill=255)
img.paste(grad, (0, 0), mask)

# Soft shadow under the shield
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(shadow).polygon(shield(S/2, 190, 600, 700), fill=(0, 0, 0, 110))
shadow = shadow.filter(ImageFilter.GaussianBlur(26))
img.alpha_composite(shadow)

# Shield: orange with a lighter left half for depth
d = ImageDraw.Draw(img)
outer = shield(S/2, 160, 600, 700)
d.polygon(outer, fill=(240, 162, 63, 255))
half = Image.new("L", (S, S), 0)
ImageDraw.Draw(half).polygon(outer, fill=255)
ImageDraw.Draw(half).rectangle([S/2, 0, S, S], fill=0)
light = Image.new("RGBA", (S, S), (255, 196, 110, 255))
img.paste(light, (0, 0), half)

# Inner rim
d = ImageDraw.Draw(img)
d.line(shield(S/2, 215, 500, 590) + [shield(S/2, 215, 500, 590)[0]], fill=(255, 255, 255, 90), width=14, joint="curve")

# White check mark with round caps
pts = [(370, 520), (475, 628), (668, 412)]
d.line(pts, fill=(255, 255, 255, 255), width=74, joint="curve")
for p in (pts[0], pts[2]):
    d.ellipse([p[0] - 37, p[1] - 37, p[0] + 37, p[1] + 37], fill=(255, 255, 255, 255))

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Guardrail.Desktop", "Assets")
os.makedirs(out, exist_ok=True)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(out, "guardrail.png"))
img.save(os.path.join(out, "guardrail.ico"), sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((512, 512), Image.LANCZOS).save(os.path.join(out, "..", "..", "..", "tools", "icon-preview.png"))
print("ok")
