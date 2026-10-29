#!/usr/bin/env python3
"""Generate the seamless surface maps used by the MMO graybox kit.

Surfaces: grass, cobblestone, worn cobblestone, generic wall, roof tiles,
and the crafting-station materials (anvil metal, furnace stone). The output
is deterministic for a given seed so a rebake does not churn the PNGs.

    python3 tools/generate_ground_textures.py --kind grass
    python3 tools/generate_ground_textures.py --kind cobblestone
    python3 tools/generate_ground_textures.py --kind cobblestone-worn
    python3 tools/generate_ground_textures.py --kind anvil
    python3 tools/generate_ground_textures.py --kind furnace

Ground and building maps land under Assets/Textures/Ground and
Assets/Textures/Building; crafting maps land under Assets/Textures/Crafting.

Outputs per surface:
    {Surface}_Albedo.png          sRGB base colour
    {Surface}_Normal.png          tangent-space normal map (linear)
    {Surface}_Mask.png            R metallic, G ambient occlusion,
                                  A smoothness (linear)
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUT = REPO_ROOT / "Assets" / "Textures" / "Ground"
CRAFTING_OUT = REPO_ROOT / "Assets" / "Textures" / "Crafting"


def periodic_value_noise(size: int, period: int, rng: np.random.Generator) -> np.ndarray:
    """Seamless value noise on a size x size grid, repeating every `period` cells."""
    grid = rng.random((period, period), dtype=np.float32)
    coords = np.arange(size, dtype=np.float32) * (period / size)
    x0 = np.floor(coords).astype(np.int32) % period
    y0 = np.floor(coords).astype(np.int32) % period
    x1 = (x0 + 1) % period
    y1 = (y0 + 1) % period

    fx = coords - np.floor(coords)
    fy = coords - np.floor(coords)
    fx = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0)
    fy = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0)

    g00 = grid[np.ix_(y0, x0)]
    g10 = grid[np.ix_(y0, x1)]
    g01 = grid[np.ix_(y1, x0)]
    g11 = grid[np.ix_(y1, x1)]

    top = g00 + (g10 - g00) * fx[None, :]
    bottom = g01 + (g11 - g01) * fx[None, :]
    return top + (bottom - top) * fy[:, None]


def fbm(size: int, rng: np.random.Generator, periods: tuple[int, ...], weights: tuple[float, ...]) -> np.ndarray:
    total = np.zeros((size, size), dtype=np.float32)
    for period, weight in zip(periods, weights):
        total += periodic_value_noise(size, period, rng) * weight
    total -= total.min()
    peak = total.max()
    return total / peak if peak > 0.0 else total


def gaussian_blur_wrap(image: np.ndarray, sigma: float) -> np.ndarray:
    """Separable Gaussian blur with wrap edges, keeping float precision."""
    if sigma <= 0.0:
        return image
    radius = max(1, int(math.ceil(sigma * 3.0)))
    offsets = np.arange(-radius, radius + 1, dtype=np.float32)
    kernel = np.exp(-0.5 * (offsets / sigma) ** 2)
    kernel /= kernel.sum()

    size = image.shape[0]
    padded = np.pad(image, ((radius, radius), (0, 0)), mode="wrap")
    rows = np.zeros_like(image)
    for index, weight in enumerate(kernel):
        rows += padded[index:index + size] * weight

    padded = np.pad(rows, ((0, 0), (radius, radius)), mode="wrap")
    columns = np.zeros_like(image)
    for index, weight in enumerate(kernel):
        columns += padded[:, index:index + size] * weight
    return columns


def smoothstep(edge0: float, edge1: float, value: np.ndarray) -> np.ndarray:
    t = np.clip((value - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def color_ramp(values: np.ndarray, stops: list[float], colors: list[tuple[float, float, float]]) -> np.ndarray:
    positions = np.array(stops, dtype=np.float32)
    palette = np.array(colors, dtype=np.float32)
    out = np.empty(values.shape + (3,), dtype=np.float32)
    for channel in range(3):
        out[..., channel] = np.interp(values, positions, palette[:, channel])
    return out


def wrap_offsets(size: int, x: float, y: float, radius: float) -> list[tuple[float, float]]:
    offsets = [(0, 0)]
    if x - radius < 0:
        offsets.append((size, 0))
    if x + radius > size:
        offsets.append((-size, 0))
    if y - radius < 0:
        offsets.append((0, size))
    if y + radius > size:
        offsets.append((0, -size))
    if x - radius < 0 and y - radius < 0:
        offsets.append((size, size))
    if x + radius > size and y - radius < 0:
        offsets.append((-size, size))
    if x - radius < 0 and y + radius > size:
        offsets.append((size, -size))
    if x + radius > size and y + radius > size:
        offsets.append((-size, -size))
    return offsets


def paint_blobs(size: int, rng: np.random.Generator, palette: list[tuple[int, int, int]], count: int,
                min_radius: int, max_radius: int, min_alpha: int, max_alpha: int) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for _ in range(count):
        radius = int(rng.integers(min_radius, max_radius + 1))
        x = float(rng.uniform(0, size))
        y = float(rng.uniform(0, size))
        color = palette[int(rng.integers(0, len(palette)))]
        alpha = int(rng.integers(min_alpha, max_alpha + 1))
        squash = float(rng.uniform(0.55, 1.0))
        for dx, dy in wrap_offsets(size, x, y, radius):
            draw.ellipse(
                [x + dx - radius, y + dy - radius * squash, x + dx + radius, y + dy + radius * squash],
                fill=(*color, alpha),
            )
    return layer.filter(ImageFilter.GaussianBlur(radius=max(1, size // 256)))


def paint_tufts(size: int, rng: np.random.Generator, count: int) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    light = (168, 196, 112)
    dark = (48, 82, 42)
    for _ in range(count):
        x = float(rng.uniform(0, size))
        y = float(rng.uniform(0, size))
        blades = int(rng.integers(4, 10))
        for _ in range(blades):
            angle = -math.pi / 2.0 + float(rng.uniform(-0.85, 0.85))
            length = float(rng.uniform(size * 0.008, size * 0.022))
            lean = float(rng.uniform(-0.25, 0.25))
            tip_x = x + math.cos(angle) * length
            tip_y = y + math.sin(angle) * length
            mid_x = x + math.cos(angle + lean) * length * 0.55
            mid_y = y + math.sin(angle + lean) * length * 0.55
            color = light if rng.random() < 0.55 else dark
            alpha = int(rng.integers(30, 75))
            width = 1 if size < 1500 else 2
            for dx, dy in wrap_offsets(size, x, y, length):
                draw.line(
                    [(x + dx, y + dy), (mid_x + dx, mid_y + dy), (tip_x + dx, tip_y + dy)],
                    fill=(*color, alpha),
                    width=width,
                    joint="curve",
                )
    return layer.filter(ImageFilter.GaussianBlur(radius=0.35))


def combine_layers(base: np.ndarray, layer: Image.Image) -> np.ndarray:
    overlay = np.asarray(layer, dtype=np.float32) / 255.0
    alpha = overlay[..., 3:4]
    return base * (1.0 - alpha) + overlay[..., :3] * alpha


def combine_layers(base: np.ndarray, layer: Image.Image) -> np.ndarray:
    overlay = np.asarray(layer, dtype=np.float32) / 255.0
    alpha = overlay[..., 3:4]
    return base * (1.0 - alpha) + overlay[..., :3] * alpha


def normal_from_height(height: np.ndarray, strength: float) -> np.ndarray:
    dhdx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dhdy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    normal = np.dstack((-dhdx * strength, -dhdy * strength, np.ones_like(height)))
    length = np.sqrt((normal * normal).sum(axis=2, keepdims=True))
    return normal / np.maximum(length, 1e-6)


def periodic_voronoi(size: int, cells: int, rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Seamless Voronoi: nearest distance, second distance and nearest cell id."""
    jitter = rng.uniform(0.12, 0.88, size=(cells, cells, 2)).astype(np.float32)
    coords = (np.arange(size, dtype=np.float32) + 0.5) / size
    u = coords[None, :]
    v = coords[:, None]

    first = np.full((size, size), 1e9, dtype=np.float32)
    second = np.full((size, size), 1e9, dtype=np.float32)
    ids = np.zeros((size, size), dtype=np.int32)

    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            ix = np.floor(u * cells).astype(np.int32) + ox
            iy = np.floor(v * cells).astype(np.int32) + oy
            wx = ix % cells
            wy = iy % cells
            px = (ix + jitter[wy, wx, 0]) / cells
            py = (iy + jitter[wy, wx, 1]) / cells
            dx = px - u
            dy = py - v
            dx -= np.round(dx)
            dy -= np.round(dy)
            distance = np.sqrt(dx * dx + dy * dy)

            closer = distance < first
            second = np.where(closer, first, np.minimum(second, distance))
            first = np.where(closer, distance, first)
            ids = np.where(closer, wy * cells + wx, ids)

    return first, second, ids


def save_png(path: Path, array: np.ndarray, channels: str) -> None:
    if channels == "RGB":
        array = array[..., :3]
    data = np.clip(array * 255.0 + 0.5, 0.0, 255.0).astype(np.uint8)
    Image.fromarray(data, mode=channels).save(path, optimize=True)


def generate_grass(out_dir: Path, size: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)

    broad = fbm(size, rng, (6, 12, 24, 48), (0.30, 0.30, 0.24, 0.16))
    fine = fbm(size, rng, (12, 24, 48, 96), (0.44, 0.30, 0.18, 0.08))

    tone = np.clip(
        0.5 + (broad - 0.5) * 0.20 + (fine - 0.5) * 0.22,
        0.0,
        1.0,
    )

    grass_ramp = [
        (0.0, (0.345, 0.490, 0.250)),
        (0.5, (0.395, 0.550, 0.292)),
        (1.0, (0.455, 0.615, 0.340)),
    ]

    base = color_ramp(tone, [s for s, _ in grass_ramp], [c for _, c in grass_ramp])
    base *= (0.99 + 0.02 * fine[..., None])

    tufts = paint_tufts(size, rng, count=max(90, size // 3))
    base = combine_layers(base, tufts)

    height = 0.12 * broad + 0.06 * fine
    height += np.asarray(tufts.getchannel("A"), dtype=np.float32) / 255.0 * 0.45
    height = height.astype(np.float32)
    height = gaussian_blur_wrap(height, sigma=max(0.5, size / 1800.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.014)
    normal = normal * 0.5 + 0.5

    ao = (0.95 + 0.05 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.18 + 0.06 * fine).astype(np.float32)
    mask = np.dstack((np.zeros_like(height), ao, np.zeros_like(height), smoothness))

    save_png(out_dir / "Grass_Albedo.png", base, "RGB")
    save_png(out_dir / "Grass_Normal.png", normal, "RGB")
    save_png(out_dir / "Grass_Mask.png", mask, "RGBA")


def generate_cobblestone(out_dir: Path, size: int, seed: int, worn: bool = False) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)
    prefix = "Cobblestone_Worn" if worn else "Cobblestone"

    cells = 7
    first, second, ids = periodic_voronoi(size, cells, rng)
    edge = second - first

    mortar = 1.0 - smoothstep(0.006, 0.017, edge)
    stone = 1.0 - mortar
    bevel = smoothstep(0.010, 0.050, edge)
    dome = 1.0 - smoothstep(0.0, 0.60 / cells, first)

    cell_count = cells * cells
    palette = np.array(
        [
            (0.485, 0.445, 0.385),
            (0.405, 0.380, 0.337),
            (0.325, 0.310, 0.284),
            (0.535, 0.470, 0.380),
            (0.440, 0.440, 0.405),
            (0.285, 0.275, 0.250),
        ]
        if worn
        else
        [
            (0.545, 0.518, 0.463),
            (0.463, 0.447, 0.404),
            (0.373, 0.361, 0.333),
            (0.612, 0.565, 0.475),
            (0.498, 0.502, 0.478),
            (0.333, 0.325, 0.306),
        ],
        dtype=np.float32,
    )
    cell_color = palette[rng.integers(0, len(palette), size=cell_count)]
    stone_color = cell_color[ids]

    broad = fbm(size, rng, (2, 4, 8, 16), (0.48, 0.30, 0.16, 0.06))
    fine = fbm(size, rng, (12, 24, 48, 96), (0.44, 0.30, 0.18, 0.08))

    if worn:
        wear = fbm(size, rng, (3, 6, 12, 24), (0.44, 0.30, 0.18, 0.08))
        dirt = smoothstep(0.62, 0.88, wear)[..., None]
        dirt_color = np.array((0.185, 0.161, 0.125), dtype=np.float32)
        stone_color = stone_color * (0.86 + 0.20 * broad[..., None])
        stone_color *= (0.96 + 0.08 * fine[..., None])
        stone_color *= (0.80 + 0.26 * bevel[..., None]) * (0.86 + 0.18 * dome[..., None])
        stone_color = stone_color * (1.0 - dirt) + dirt_color * dirt * 0.35
        mortar_color = np.array((0.160, 0.145, 0.120), dtype=np.float32)
        mortar_color = mortar_color * (0.80 + 0.26 * broad[..., None])
    else:
        stone_color = stone_color * (0.88 + 0.22 * broad[..., None])
        stone_color *= (0.97 + 0.06 * fine[..., None])
        stone_color *= (0.82 + 0.28 * bevel[..., None]) * (0.88 + 0.18 * dome[..., None])
        mortar_color = np.array((0.196, 0.188, 0.169), dtype=np.float32)
        mortar_color = mortar_color * (0.82 + 0.25 * broad[..., None])

    base = stone_color * stone[..., None] + mortar_color * mortar[..., None]

    height = stone * (0.40 + 0.34 * bevel + 0.26 * dome) + 0.10 * broad + 0.04 * fine
    height += np.asarray(
        Image.fromarray(np.clip(mortar * 255.0, 0, 255).astype(np.uint8))
        .filter(ImageFilter.GaussianBlur(radius=max(0.5, size / 1400.0))),
        dtype=np.float32,
    ) / 255.0 * -0.16
    height = gaussian_blur_wrap(height.astype(np.float32), sigma=max(0.4, size / 2200.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.030)
    normal = normal * 0.5 + 0.5

    ao = (0.62 + 0.38 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.08 + 0.24 * stone * (0.75 + 0.25 * bevel)).astype(np.float32) if worn \
        else (0.12 + 0.30 * stone * (0.75 + 0.25 * bevel)).astype(np.float32)
    mask = np.dstack((np.zeros_like(height), ao, np.zeros_like(height), smoothness))

    save_png(out_dir / f"{prefix}_Albedo.png", base, "RGB")
    save_png(out_dir / f"{prefix}_Normal.png", normal, "RGB")
    save_png(out_dir / f"{prefix}_Mask.png", mask, "RGBA")


def generate_wall(out_dir: Path, size: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)

    broad = fbm(size, rng, (2, 4, 8, 16), (0.48, 0.30, 0.16, 0.06))
    mid = fbm(size, rng, (6, 12, 24, 48), (0.40, 0.30, 0.20, 0.10))
    fine = fbm(size, rng, (24, 48, 96, 192), (0.44, 0.30, 0.18, 0.08))

    wall_ramp = [
        (0.0, (0.455, 0.420, 0.345)),
        (0.36, (0.555, 0.515, 0.425)),
        (0.62, (0.640, 0.598, 0.492)),
        (0.82, (0.705, 0.660, 0.545)),
        (1.0, (0.780, 0.735, 0.615)),
    ]
    base = color_ramp(broad * 0.62 + mid * 0.38, [s for s, _ in wall_ramp], [c for _, c in wall_ramp])
    base *= (0.965 + 0.06 * fine[..., None])

    stains = paint_blobs(
        size,
        rng,
        palette=[(105, 82, 52), (88, 72, 50), (124, 103, 69)],
        count=max(8, size // 90),
        min_radius=max(18, size // 32),
        max_radius=max(55, size // 10),
        min_alpha=3,
        max_alpha=8,
    )
    base = combine_layers(base, stains)

    height = broad * 0.55 + mid * 0.30 + fine * 0.15
    height += np.asarray(stains.getchannel("A"), dtype=np.float32) / 255.0 * 0.08
    height = gaussian_blur_wrap(height.astype(np.float32), sigma=max(0.4, size / 2200.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.012)
    normal = normal * 0.5 + 0.5

    ao = (0.92 + 0.08 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.14 + 0.10 * mid).astype(np.float32)
    mask = np.dstack((np.zeros_like(height), ao, np.zeros_like(height), smoothness))

    save_png(out_dir / "Wall_Generic_Albedo.png", base, "RGB")
    save_png(out_dir / "Wall_Generic_Normal.png", normal, "RGB")
    save_png(out_dir / "Wall_Generic_Mask.png", mask, "RGBA")


def generate_roof_tiles(out_dir: Path, size: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)

    columns = 5
    rows = 9
    coords = (np.arange(size, dtype=np.float32) + 0.5) / size
    u = coords[None, :]
    v = coords[:, None]
    tile_u = np.mod(u * columns, 1.0)
    tile_v = np.mod(v * rows, 1.0)
    tile_x = np.floor(u * columns).astype(np.int32)
    tile_y = np.floor(v * rows).astype(np.int32)
    tile_id = tile_y * columns + (tile_x % columns)

    palette = np.array(
        [
            (0.430, 0.190, 0.115),
            (0.505, 0.232, 0.132),
            (0.360, 0.158, 0.105),
            (0.480, 0.255, 0.158),
            (0.555, 0.280, 0.160),
            (0.330, 0.150, 0.110),
        ],
        dtype=np.float32,
    )
    tile_color = palette[rng.integers(0, len(palette), size=rows * columns)]
    base = tile_color[tile_id]

    dome = np.sin(np.pi * tile_u)
    row_lip = smoothstep(0.78, 0.98, tile_v)
    seam_u = smoothstep(0.0, 0.045, tile_u) * smoothstep(0.0, 0.045, 1.0 - tile_u)
    seam_v = smoothstep(0.0, 0.040, tile_v) * smoothstep(0.0, 0.060, 1.0 - tile_v)
    seam = seam_u * seam_v

    broad = fbm(size, rng, (2, 4, 8, 16), (0.48, 0.30, 0.16, 0.06))
    fine = fbm(size, rng, (12, 24, 48, 96), (0.44, 0.30, 0.18, 0.08))

    base = base * (0.88 + 0.20 * broad[..., None])
    base *= (0.96 + 0.08 * fine[..., None])
    base *= (0.86 + 0.18 * dome[..., None])
    shadow = (1.0 - seam)[..., None] * 0.58
    base = base * (1.0 - shadow) + np.array((0.13, 0.075, 0.045), dtype=np.float32) * shadow

    height = (0.35 + 0.45 * dome + 0.20 * row_lip) * seam
    height += broad * 0.10 + fine * 0.04
    height = gaussian_blur_wrap(height.astype(np.float32), sigma=max(0.4, size / 2000.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.030)
    normal = normal * 0.5 + 0.5

    ao = (0.58 + 0.42 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.10 + 0.20 * seam * (0.70 + 0.30 * dome)).astype(np.float32)
    mask = np.dstack((np.zeros_like(height), ao, np.zeros_like(height), smoothness))

    save_png(out_dir / "RoofTiles_Albedo.png", base, "RGB")
    save_png(out_dir / "RoofTiles_Normal.png", normal, "RGB")
    save_png(out_dir / "RoofTiles_Mask.png", mask, "RGBA")


def generate_anvil(out_dir: Path, size: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)

    broad = fbm(size, rng, (3, 6, 12, 24), (0.40, 0.30, 0.20, 0.10))
    fine = fbm(size, rng, (16, 32, 64, 128), (0.40, 0.30, 0.20, 0.10))
    first, _, _ = periodic_voronoi(size, 18, rng)
    dimple = 1.0 - smoothstep(0.0, 0.60 / 18.0, first)

    metal_ramp = [
        (0.0, (0.110, 0.115, 0.125)),
        (0.45, (0.170, 0.180, 0.195)),
        (0.78, (0.240, 0.250, 0.270)),
        (1.0, (0.290, 0.300, 0.320)),
    ]
    base = color_ramp(
        np.clip(0.55 * broad + 0.45 * fine, 0.0, 1.0),
        [s for s, _ in metal_ramp],
        [c for _, c in metal_ramp],
    )
    base *= (0.94 + 0.09 * dimple[..., None])

    rust_amount = smoothstep(0.64, 0.88, broad) * 0.45
    rust_color = np.array((0.225, 0.115, 0.055), dtype=np.float32)
    base = base * (1.0 - rust_amount[..., None]) + rust_color * rust_amount[..., None]
    base *= (0.965 + 0.07 * fine[..., None])

    height = 0.30 * fine + 0.18 * broad + 0.30 * dimple
    height = gaussian_blur_wrap(height.astype(np.float32), sigma=max(0.5, size / 2400.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.022)
    normal = normal * 0.5 + 0.5

    ao = (0.80 + 0.20 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.62 - 0.35 * rust_amount + 0.10 * dimple).astype(np.float32)
    metallic = (1.0 - 0.80 * rust_amount).astype(np.float32)
    mask = np.dstack((metallic, ao, np.zeros_like(height), smoothness))

    save_png(out_dir / "Anvil_Albedo.png", base, "RGB")
    save_png(out_dir / "Anvil_Normal.png", normal, "RGB")
    save_png(out_dir / "Anvil_Mask.png", mask, "RGBA")


def generate_furnace(out_dir: Path, size: int, seed: int) -> None:
    rng = np.random.default_rng(seed)
    out_dir.mkdir(parents=True, exist_ok=True)

    rows = 6
    cols = 4
    coords = (np.arange(size, dtype=np.float32) + 0.5) / size
    u = coords[None, :]
    v = coords[:, None]

    row = np.floor(v * rows).astype(np.int32)
    fu = u * cols + (row % 2) * 0.5
    col = np.floor(fu).astype(np.int32) % cols
    tu = fu - np.floor(fu)
    tv = v * rows - row

    margin_u = 0.030
    margin_v = 0.060
    seam_u = smoothstep(0.0, margin_u, tu) * smoothstep(0.0, margin_u, 1.0 - tu)
    seam_v = smoothstep(0.0, margin_v, tv) * smoothstep(0.0, margin_v, 1.0 - tv)
    stone = seam_u * seam_v
    mortar = 1.0 - stone
    bevel = smoothstep(0.0, 0.12, tu) * smoothstep(0.0, 0.12, 1.0 - tu)
    bevel *= smoothstep(0.0, 0.20, tv) * smoothstep(0.0, 0.20, 1.0 - tv)

    broad = fbm(size, rng, (3, 6, 12, 24), (0.40, 0.30, 0.20, 0.10))
    fine = fbm(size, rng, (16, 32, 64, 128), (0.40, 0.30, 0.20, 0.10))

    palette = np.array(
        [
            (0.255, 0.235, 0.215),
            (0.315, 0.290, 0.265),
            (0.215, 0.200, 0.185),
            (0.360, 0.325, 0.290),
            (0.285, 0.260, 0.235),
        ],
        dtype=np.float32,
    )
    brick_color = palette[rng.integers(0, len(palette), size=rows * cols)]
    brick = brick_color[row * cols + col]
    brick *= (0.90 + 0.16 * broad[..., None])
    brick *= (0.97 + 0.05 * fine[..., None])
    brick *= (0.84 + 0.20 * bevel[..., None])

    soot = smoothstep(0.58, 0.86, broad)[..., None] * 0.42
    soot_color = np.array((0.055, 0.052, 0.050), dtype=np.float32)
    brick = brick * (1.0 - soot) + soot_color * soot
    mortar_color = np.array((0.125, 0.118, 0.110), dtype=np.float32)

    base = brick * stone[..., None] + mortar_color * mortar[..., None]

    height = stone * (0.50 + 0.35 * bevel) + 0.10 * broad + 0.05 * fine
    height += np.asarray(
        Image.fromarray(np.clip(mortar * 255.0, 0, 255).astype(np.uint8))
        .filter(ImageFilter.GaussianBlur(radius=max(0.5, size / 1600.0))),
        dtype=np.float32,
    ) / 255.0 * -0.10
    height = gaussian_blur_wrap(height.astype(np.float32), sigma=max(0.4, size / 2200.0))
    height -= height.min()
    height /= max(height.max(), 1e-6)

    normal = normal_from_height(height, strength=size * 0.030)
    normal = normal * 0.5 + 0.5

    ao = (0.65 + 0.35 * smoothstep(0.05, 0.95, height)).astype(np.float32)
    smoothness = (0.10 + 0.10 * fine).astype(np.float32)
    mask = np.dstack((np.zeros_like(height), ao, np.zeros_like(height), smoothness))

    save_png(out_dir / "Furnace_Albedo.png", base, "RGB")
    save_png(out_dir / "Furnace_Normal.png", normal, "RGB")
    save_png(out_dir / "Furnace_Mask.png", mask, "RGBA")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--kind",
        choices=("grass", "cobblestone", "cobblestone-worn", "wall", "roof", "anvil", "furnace", "all"),
        default="grass")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--seed", type=int, default=20261028)
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    if args.size < 256 or args.size & (args.size - 1):
        parser.error("--size must be a power of two of at least 256")

    def target(kind: str) -> Path:
        if args.out is not None:
            return args.out
        return CRAFTING_OUT if kind in ("anvil", "furnace") else DEFAULT_OUT

    if args.kind in ("grass", "all"):
        generate_grass(target("grass"), args.size, args.seed)
    if args.kind in ("cobblestone", "all"):
        generate_cobblestone(target("cobblestone"), args.size, args.seed)
    if args.kind in ("cobblestone-worn", "all"):
        generate_cobblestone(target("cobblestone-worn"), args.size, args.seed + 1, worn=True)
    if args.kind in ("wall", "all"):
        generate_wall(target("wall"), args.size, args.seed + 2)
    if args.kind in ("roof", "all"):
        generate_roof_tiles(target("roof"), args.size, args.seed + 3)
    if args.kind in ("anvil", "all"):
        generate_anvil(target("anvil"), args.size, args.seed + 4)
    if args.kind in ("furnace", "all"):
        generate_furnace(target("furnace"), args.size, args.seed + 5)


if __name__ == "__main__":
    main()
