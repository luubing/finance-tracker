"""从渲染好的设计稿里裁一块出来看细节（坐标用 CSS 像素，输出保持 @2x）。
用法：python .scratch/cold-start/crop.py <png 名> <x> <y> <w> <h> <输出名>
例：  python .scratch/cold-start/crop.py 01-cold-start.png 73 200 900 900 crop-a
"""
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent
DOCS = ROOT.parents[1] / "docs" / "design" / "cold-start"


def main() -> None:
    name, x, y, w, h, out = sys.argv[1], *map(float, sys.argv[2:6]), sys.argv[6]
    img = Image.open(DOCS / name)
    s = 2  # DPR
    box = (int(x * s), int(y * s), int((x + w) * s), int((y + h) * s))
    crop = img.crop(box)
    (ROOT / "assets").mkdir(exist_ok=True)
    dst = ROOT / "assets" / f"{out}.png"
    crop.save(dst)
    print("wrote", dst, crop.size)


if __name__ == "__main__":
    main()
