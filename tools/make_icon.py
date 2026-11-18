# 生成 app.ico（纯标准库，圆角方块 + 电池图形，4x 超采样抗锯齿）
import math
import os
import struct

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "app.ico")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 4  # supersample


def lerp(a, b, t):
    return a + (b - a) * t


def rounded_rect_contains(x, y, x0, y0, x1, y1, r):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r + 1e-9


def render(size):
    n = size * SS
    px = [[(0, 0, 0, 0) for _ in range(n)] for _ in range(n)]

    pad = n * 0.02
    x0, y0, x1, y1 = pad, pad, n - pad, n - pad
    r = n * 0.235

    # 背景渐变（左上亮蓝 -> 右下深蓝）
    for y in range(n):
        for x in range(n):
            if not rounded_rect_contains(x, y, x0, y0, x1, y1, r):
                continue
            t = (x / n * 0.35 + y / n * 0.65)
            rr = int(lerp(0x5E, 0x35, t))
            gg = int(lerp(0x9F, 0x64, t))
            bb = int(lerp(0xFF, 0xC8, t))
            # 顶部高光
            if y < n * 0.42:
                a = (1 - y / (n * 0.42)) * 0.10
                rr = int(lerp(rr, 255, a))
                gg = int(lerp(gg, 255, a))
                bb = int(lerp(bb, 255, a))
            px[y][x] = (rr, gg, bb, 255)

    # 电池：白色圆角矩形 + 正极 + 电量填充
    bw, bh = n * 0.50, n * 0.34
    bx = (n - bw) / 2 - n * 0.035
    by = (n - bh) / 2
    br = n * 0.075
    sw = max(1.0, n * 0.052)  # 边框粗细
    tip_w, tip_h = n * 0.045, bh * 0.42

    def battery_outline(x, y):
        outer = rounded_rect_contains(x, y, bx, by, bx + bw, by + bh, br)
        inner = rounded_rect_contains(x, y, bx + sw, by + sw, bx + bw - sw, by + bh - sw, max(1, br - sw))
        return outer and not inner

    def tip(x, y):
        return bx + bw <= x <= bx + bw + tip_w and by + (bh - tip_h) / 2 <= y <= by + (bh + tip_h) / 2

    fill_w = (bw - sw * 2) * 0.72
    for y in range(n):
        for x in range(n):
            if battery_outline(x, y) or tip(x, y):
                px[y][x] = (255, 255, 255, 255)
            elif rounded_rect_contains(x, y, bx + sw * 1.6, by + sw * 1.6, bx + sw * 1.6 + fill_w, by + bh - sw * 1.6, max(1, br - sw)):
                px[y][x] = (255, 255, 255, 235)

    # 闪电（深蓝）
    bolt = [
        (0.545, 0.26), (0.455, 0.53), (0.505, 0.53),
        (0.470, 0.74), (0.565, 0.46), (0.512, 0.46),
    ]
    bpts = [(x * n, y * n) for x, y in bolt]

    def in_poly(px_, py_, poly):
        inside = False
        j = len(poly) - 1
        for i in range(len(poly)):
            xi, yi = poly[i]
            xj, yj = poly[j]
            if (yi > py_) != (yj > py_) and px_ < (xj - xi) * (py_ - yi) / (yj - yi + 1e-12) + xi:
                inside = not inside
            j = i
        return inside

    for y in range(n):
        for x in range(n):
            if in_poly(x, y, bpts):
                px[y][x] = (0x2C, 0x53, 0xB8, 255)

    # 缩小（盒式滤波）
    out = [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]
    for oy in range(size):
        for ox in range(size):
            sr = sg = sb = sa = 0
            for dy in range(SS):
                for dx in range(SS):
                    r_, g_, b_, a_ = px[oy * SS + dy][ox * SS + dx]
                    sr += r_ * a_
                    sg += g_ * a_
                    sb += b_ * a_
                    sa += a_
            if sa == 0:
                out[oy][ox] = (0, 0, 0, 0)
            else:
                out[oy][ox] = (int(sr / sa), int(sg / sa), int(sb / sa), int(sa / (SS * SS)))
    return out


def write_ico(path, images):
    entries = []
    blobs = []
    offset = 6 + 16 * len(images)
    for size, img in images:
        row_bytes = size * 4
        xor = bytearray()
        for y in range(size - 1, -1, -1):
            for x in range(size):
                r, g, b, a = img[y][x]
                xor += bytes((b, g, r, a))
        and_row = ((size + 31) // 32) * 4
        and_mask = bytearray(and_row * size)
        header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0)
        blob = header + bytes(xor) + bytes(and_mask)
        entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(blob), offset))
        blobs.append(blob)
        offset += len(blob)
    with open(path, 'wb') as f:
        f.write(struct.pack('<HHH', 0, 1, len(images)))
        for e in entries:
            f.write(e)
        for b in blobs:
            f.write(b)


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    images = []
    for s in SIZES:
        print('render', s)
        images.append((s, render(s)))
    write_ico(OUT, images)
    print('written', OUT, os.path.getsize(OUT), 'bytes')


if __name__ == '__main__':
    main()
