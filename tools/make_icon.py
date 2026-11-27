"""アプリアイコン (app.ico) を生成するスクリプト。python3 tools/make_icon.py で再生成できる（要 Pillow）。"""
from PIL import Image, ImageDraw
import os

def draw(size):
    s = size * 4  # supersample
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = s // 5
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=(36, 41, 56, 255))
    # 3本のバー = プロジェクト一覧、一番上だけオレンジ（未コミット）
    pad = s * 0.2
    h = s * 0.13
    gap = s * 0.08
    colors = [(255, 140, 0, 255), (120, 200, 255, 255), (120, 200, 255, 255)]
    for i, c in enumerate(colors):
        y = pad + i * (h + gap) + s * 0.04
        d.rounded_rectangle([pad, y, s - pad, y + h], radius=h / 2, fill=c)
    return img.resize((size, size), Image.LANCZOS)

out = os.path.join(os.path.dirname(__file__), "..", "src", "ProjectLauncher.App", "Assets", "app.ico")
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
base = draw(256)
base.save(out, sizes=[(n, n) for n in sizes], append_images=[draw(n) for n in sizes[:-1]])
print("wrote", os.path.abspath(out))
