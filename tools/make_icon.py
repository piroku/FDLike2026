"""FDLike2026 のアプリアイコンを生成する（WinFM Classic 配色: 紺地・シアン枠・黄色文字）。
python tools/make_icon.py → app.ico / docs/icon_256.png
"""
from PIL import Image, ImageDraw, ImageFont
import os

NAVY = (0, 0, 64, 255)
CYAN = (0, 255, 255, 255)
YELLOW = (255, 255, 0, 255)
WHITE = (255, 255, 255, 255)
CURSOR = (0, 0, 255, 255)

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"


def render(size: int) -> Image.Image:
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = 28
    # 紺色の角丸タイル
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=r, fill=NAVY)
    # シアンの二重枠（WinFM のフレーム）
    d.rounded_rectangle((10, 10, S - 11, S - 11), radius=r - 8, outline=CYAN, width=6)
    d.rounded_rectangle((22, 22, S - 23, S - 23), radius=r - 16, outline=CYAN, width=2)
    # 中央の縦の仕切り（2 ペイン）
    d.line((S // 2, 34, S // 2, S - 35), fill=CYAN, width=4)
    # 左ペイン: カーソル行（青帯）と行の目印
    d.rectangle((34, 150, S // 2 - 10, 176), fill=CURSOR)
    for y in (64, 92, 120, 198):
        d.rectangle((40, y, S // 2 - 18, y + 10), fill=(0, 200, 200, 255))
    d.rectangle((40, 157, S // 2 - 18, 169), fill=WHITE)
    # 右ペインに大きく "FD"
    font = ImageFont.truetype(FONT_BOLD, 108)
    txt = "FD"
    bbox = d.textbbox((0, 0), txt, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    cx = S // 2 + (S // 2 - 22) // 2 + 8
    cy = S // 2
    d.text((cx - tw / 2 - bbox[0], cy - th / 2 - bbox[1]), txt, font=font, fill=YELLOW)
    return img.resize((size, size), Image.LANCZOS) if size != S else img


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [render(s) for s in sizes]
    ico = os.path.join(root, "app.ico")
    imgs[-1].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    os.makedirs(os.path.join(root, "docs"), exist_ok=True)
    imgs[-1].save(os.path.join(root, "docs", "icon_256.png"))
    print("wrote", ico)


if __name__ == "__main__":
    main()
