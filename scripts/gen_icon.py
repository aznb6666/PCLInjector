"""生成 PCL 验证补丁工具的图标（多尺寸 ICO + PNG 预览）。
仅使用 Python 标准库：手工绘制像素、编码 PNG、组装 ICO。
"""
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ICO_PATH = ROOT / "icon.ico"
PREVIEW_PATH = ROOT / "ui-preview" / "icon-preview.png"

BASE = 256          # 设计画布
SS = 4              # 超采样倍率
R = BASE * SS       # 高分辨率渲染尺寸 1024

SIZES = [16, 24, 32, 48, 64, 128, 256]


def lerp(a, b, t):
    return a + (b - a) * t


def in_rounded_rect(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def in_shield(x, y):
    """盾牌：顶部平直，两侧向下，底部收为尖角。"""
    if y < 66 or y > 206:
        return False
    if y <= 126:
        left, right = 70.0, 186.0
    else:
        t = (y - 126) / 80.0
        left = (1 - t) ** 2 * 70 + 2 * (1 - t) * t * 70 + t * t * 128
        right = (1 - t) ** 2 * 186 + 2 * (1 - t) * t * 186 + t * t * 128
    return left <= x <= right


def dist_to_segment(px, py, x1, y1, x2, y2):
    dx, dy = x2 - x1, y2 - y1
    length_sq = dx * dx + dy * dy
    t = max(0.0, min(1.0, ((px - x1) * dx + (py - y1) * dy) / length_sq))
    qx, qy = x1 + t * dx, y1 + t * dy
    return ((px - qx) ** 2 + (py - qy) ** 2) ** 0.5


def render_hi():
    """在高分辨率画布上逐像素绘制，返回 RGBA bytearray。"""
    buf = bytearray(R * R * 4)
    for py in range(R):
        cy = (py + 0.5) / SS
        for px in range(R):
            cx = (px + 0.5) / SS
            idx = (py * R + px) * 4

            # 1) 背景：蓝色渐变圆角方块
            if in_rounded_rect(cx, cy, 8, 8, 248, 248, 52):
                t = min(1.0, max(0.0, (cx + cy) / (2 * BASE)))
                buf[idx] = int(lerp(59, 29, t))
                buf[idx + 1] = int(lerp(130, 78, t))
                buf[idx + 2] = int(lerp(246, 216, t))
                buf[idx + 3] = 255

                # 2) 盾牌：白色
                if in_shield(cx, cy):
                    buf[idx] = 255
                    buf[idx + 1] = 255
                    buf[idx + 2] = 255

                    # 3) 对勾：蓝色（圆头粗线）
                    half = 17.0 / 2
                    if (dist_to_segment(cx, cy, 98, 134, 120, 157) <= half or
                            dist_to_segment(cx, cy, 120, 157, 160, 107) <= half):
                        buf[idx] = 37
                        buf[idx + 1] = 99
                        buf[idx + 2] = 235
    return buf


def downsample(hi, size):
    """盒式滤波下采样，高分辨率超采样天然产生抗锯齿。"""
    factor = R // size
    out = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            acc = [0, 0, 0, 0]
            for yy in range(y * factor, (y + 1) * factor):
                row = yy * R * 4
                for xx in range(x * factor, (x + 1) * factor):
                    i = row + xx * 4
                    acc[0] += hi[i]
                    acc[1] += hi[i + 1]
                    acc[2] += hi[i + 2]
                    acc[3] += hi[i + 3]
            n = factor * factor
            o = (y * size + x) * 4
            for c in range(4):
                out[o + c] = acc[c] // n
    return out


def png_chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data +
            struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def make_png(size, rgba):
    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4]
                   for y in range(size))
    return sig + png_chunk(b"IHDR", ihdr) + png_chunk(b"IDAT", zlib.compress(raw, 9)) + png_chunk(b"IEND", b"")


def make_ico(images):
    """ICO 条目直接使用 PNG 数据（Windows Vista+ 支持）。"""
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    for size, png in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        offset += len(png)
    return header + entries + b"".join(png for _, png in images)


def main():
    print("rendering high-res icon ...")
    hi = render_hi()
    images = []
    for size in SIZES:
        rgba = downsample(hi, size)
        images.append((size, make_png(size, rgba)))
        print(f"  generated {size}x{size}")

    ICO_PATH.write_bytes(make_ico(images))
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)
    PREVIEW_PATH.write_bytes(dict(images)[256])
    print(f"ICO  -> {ICO_PATH} ({ICO_PATH.stat().st_size} bytes)")
    print(f"PNG  -> {PREVIEW_PATH}")


if __name__ == "__main__":
    main()
