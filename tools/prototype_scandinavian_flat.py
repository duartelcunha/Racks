import os
from PIL import Image, ImageDraw

BG_COLOR = (120, 120, 130, 255)  # Exact reference slate gray
DARK_BG_COLOR = (38, 40, 46, 255) # Dark slate variant for dark theme taskbar
WHITE = (255, 255, 255, 255)

# --- Concept A: 2x2 Desktop Organizer Quadrant Mark ---
# Quadrant 1 (Top-Left): Chunky Folder with tab
# Quadrant 2 (Top-Right): Folded Document Sheet
# Quadrant 3 (Bottom-Left): Grid Window (Rack layout)
# Quadrant 4 (Bottom-Right): Storage Tray / Rack holding items (homage to bowl & bottle)
def render_concept_a(size=1024, bg_color=BG_COLOR):
    canvas = Image.new("RGBA", (size, size), bg_color)
    draw = ImageDraw.Draw(canvas)

    # Margins and grid
    margin = size * 0.16
    grid_w = size - 2 * margin
    quad_w = grid_w * 0.42
    gap = grid_w * 0.16

    col0 = margin
    col1 = margin + quad_w + gap
    row0 = margin
    row1 = margin + quad_w + gap

    # 1. Top-Left: Chunky Folder
    # Tab
    tab_w = quad_w * 0.52
    tab_h = quad_w * 0.28
    draw.rounded_rectangle([col0, row0 + quad_w * 0.05, col0 + tab_w, row0 + tab_h + quad_w * 0.1], 
                           radius=int(quad_w * 0.12), fill=WHITE)
    # Body
    draw.rounded_rectangle([col0, row0 + quad_w * 0.20, col0 + quad_w, row0 + quad_w * 0.95], 
                           radius=int(quad_w * 0.14), fill=WHITE)

    # 2. Top-Right: Folded Sheet / Document with rounded folded ear
    doc_l = col1 + quad_w * 0.10
    doc_r = col1 + quad_w * 0.90
    doc_t = row0 + quad_w * 0.05
    doc_b = row0 + quad_w * 0.95
    fold_s = quad_w * 0.28
    
    # Document main polygon with cut corner
    doc_poly = [
        (doc_l, doc_t + quad_w * 0.12),
        (doc_l + quad_w * 0.12, doc_t),
        (doc_r - fold_s, doc_t),
        (doc_r, doc_t + fold_s),
        (doc_r, doc_b - quad_w * 0.12),
        (doc_r - quad_w * 0.12, doc_b),
        (doc_l + quad_w * 0.12, doc_b),
        (doc_l, doc_b - quad_w * 0.12)
    ]
    draw.polygon(doc_poly, fill=WHITE)
    # Smooth corners by overlaying rounded rect
    draw.rounded_rectangle([doc_l, doc_t + fold_s * 0.5, doc_r, doc_b], radius=int(quad_w * 0.12), fill=WHITE)
    draw.rounded_rectangle([doc_l, doc_t, doc_r - fold_s * 0.5, doc_b], radius=int(quad_w * 0.12), fill=WHITE)
    # The fold triangular flap in background color
    draw.polygon([(doc_r - fold_s, doc_t), (doc_r, doc_t + fold_s), (doc_r - fold_s, doc_t + fold_s)], fill=bg_color)

    # 3. Bottom-Left: Grid Window / 4 Rack Tiles
    win_l = col0
    win_t = row1 + quad_w * 0.05
    draw.rounded_rectangle([win_l, win_t, win_l + quad_w, win_t + quad_w * 0.90], 
                           radius=int(quad_w * 0.18), fill=WHITE)
    # 4 cutouts in background color
    pad_in = quad_w * 0.16
    tile_w = (quad_w - 2 * pad_in - quad_w * 0.12) / 2.0
    for r in range(2):
        for c in range(2):
            tx = win_l + pad_in + c * (tile_w + quad_w * 0.12)
            ty = win_t + pad_in + r * (tile_w + quad_w * 0.12)
            draw.rounded_rectangle([tx, ty, tx + tile_w, ty + tile_w], 
                                   radius=int(tile_w * 0.28), fill=bg_color)

    # 4. Bottom-Right: Rack Tray / Box holding files & dots (direct homage to bowl & bottle)
    tray_l = col1 + quad_w * 0.05
    tray_r = col1 + quad_w * 0.95
    tray_t = row1 + quad_w * 0.50
    tray_b = row1 + quad_w * 0.95
    # The tray base
    draw.rounded_rectangle([tray_l, tray_t, tray_r, tray_b], radius=int(quad_w * 0.12), fill=WHITE)
    
    # Items inside / rising from tray:
    # A vertical chunky file card / bookmark
    draw.rounded_rectangle([col1 + quad_w * 0.15, row1 + quad_w * 0.10, col1 + quad_w * 0.45, tray_t + quad_w * 0.15],
                           radius=int(quad_w * 0.10), fill=WHITE)
    # 3 round items / dots (floating or nested)
    dot_r = quad_w * 0.11
    # Dot 1
    draw.ellipse([col1 + quad_w * 0.65 - dot_r, row1 + quad_w * 0.22 - dot_r, 
                  col1 + quad_w * 0.65 + dot_r, row1 + quad_w * 0.22 + dot_r], fill=WHITE)
    # Dot 2
    draw.ellipse([col1 + quad_w * 0.56 - dot_r, row1 + quad_w * 0.44 - dot_r, 
                  col1 + quad_w * 0.56 + dot_r, row1 + quad_w * 0.44 + dot_r], fill=WHITE)
    # Dot 3
    draw.ellipse([col1 + quad_w * 0.78 - dot_r, row1 + quad_w * 0.44 - dot_r, 
                  col1 + quad_w * 0.78 + dot_r, row1 + quad_w * 0.44 + dot_r], fill=WHITE)

    return canvas

# --- Concept B: Chunky Modular Rack & Shelves (Pure Minimalist Form) ---
# 3 bold, chunky floating shelf blocks with rounded caps and item notches
def render_concept_b(size=1024, bg_color=BG_COLOR):
    canvas = Image.new("RGBA", (size, size), bg_color)
    draw = ImageDraw.Draw(canvas)

    shelf_w = size * 0.66
    shelf_h = size * 0.14
    shelf_r = shelf_h * 0.45
    cx = size / 2.0
    left = cx - shelf_w / 2.0
    right = cx + shelf_w / 2.0
    gap = size * 0.11

    y_mid = size / 2.0
    ys = [y_mid - shelf_h - gap, y_mid, y_mid + shelf_h + gap]

    # Shelf 1 (Top): Chunky shelf with a file cutout
    y0 = ys[0] - shelf_h / 2.0
    draw.rounded_rectangle([left, y0, right, y0 + shelf_h], radius=shelf_r, fill=WHITE)
    # 2 small chunky notch cutouts in shelf 1
    draw.rounded_rectangle([left + shelf_w * 0.22, y0 + shelf_h * 0.25, left + shelf_w * 0.42, y0 + shelf_h * 0.75], 
                           radius=shelf_r * 0.5, fill=bg_color)
    draw.rounded_rectangle([left + shelf_w * 0.58, y0 + shelf_h * 0.25, left + shelf_w * 0.78, y0 + shelf_h * 0.75], 
                           radius=shelf_r * 0.5, fill=bg_color)

    # Shelf 2 (Middle): Solid chunky hero shelf
    y1 = ys[1] - shelf_h / 2.0
    draw.rounded_rectangle([left, y1, right, y1 + shelf_h], radius=shelf_r, fill=WHITE)

    # Shelf 3 (Bottom): Chunky shelf with 3 dots cutout
    y2 = ys[2] - shelf_h / 2.0
    draw.rounded_rectangle([left, y2, right, y2 + shelf_h], radius=shelf_r, fill=WHITE)
    dot_r = shelf_h * 0.22
    for i in range(3):
        dcx = left + shelf_w * (0.30 + i * 0.20)
        dcy = y2 + shelf_h / 2.0
        draw.ellipse([dcx - dot_r, dcy - dot_r, dcx + dot_r, dcy + dot_r], fill=bg_color)

    return canvas

# --- Concept C: The Racks Container Squircle (Chunky Organic Desktop Mark) ---
# A chunky squircle containing 3 bold horizontal racks, pure flat white on slate
def render_concept_c(size=1024, bg_color=BG_COLOR):
    canvas = Image.new("RGBA", (size, size), bg_color)
    draw = ImageDraw.Draw(canvas)

    # Chunky outer squircle container (like an app badge / tray)
    pad = size * 0.15
    r = size * 0.18
    # 3 bold horizontal pills inside
    bar_w = size * 0.60
    bar_h = size * 0.13
    bar_r = bar_h / 2.0
    cx = size / 2.0
    left = cx - bar_w / 2.0
    right = cx + bar_w / 2.0

    y_mid = size / 2.0
    spacing = size * 0.20
    ys = [y_mid - spacing, y_mid, y_mid + spacing]

    for ym in ys:
        y = ym - bar_h / 2.0
        draw.rounded_rectangle([left, y, right, y + bar_h], radius=bar_r, fill=WHITE)

    return canvas

if __name__ == "__main__":
    artifact_dir = r"C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
    os.makedirs(artifact_dir, exist_ok=True)

    ca = render_concept_a(1024)
    cb = render_concept_b(1024)
    cc = render_concept_c(1024)

    ca.save(os.path.join(artifact_dir, "scandi_concept_a_quadrant.png"))
    cb.save(os.path.join(artifact_dir, "scandi_concept_b_shelves.png"))
    cc.save(os.path.join(artifact_dir, "scandi_concept_c_minimal.png"))

    # Comparison sheet
    W, H = 1200, 520
    sheet = Image.new("RGBA", (W, H), (45, 47, 54, 255))
    s_draw = ImageDraw.Draw(sheet)
    s_draw.text((60, 25), "Art Style Exploration: Scandinavian Flat Silhouette Aesthetic", fill=(240, 242, 248))

    titles = [
        ("Concept A: 2x2 Desktop Quadrant", 60),
        ("Concept B: Chunky Modular Shelves", 440),
        ("Concept C: Pure Bold Bars", 820)
    ]
    for t, x in titles:
        s_draw.text((x, 60), t, fill=(200, 205, 215))

    sheet.alpha_composite(ca.resize((320, 320), Image.Resampling.LANCZOS), (60, 95))
    sheet.alpha_composite(cb.resize((320, 320), Image.Resampling.LANCZOS), (440, 95))
    sheet.alpha_composite(cc.resize((320, 320), Image.Resampling.LANCZOS), (820, 95))

    # Add size previews (48, 32, 24, 16px) below each
    for idx, im in enumerate([ca, cb, cc]):
        x_base = 60 + idx * 380
        for s in [48, 32, 24, 16]:
            sm = im.resize((s, s), Image.Resampling.LANCZOS)
            sheet.alpha_composite(sm, (x_base, 440 + (48 - s)//2))
            x_base += s + 16

    sheet.save(os.path.join(artifact_dir, "scandi_art_style_comparison.png"))
    print("Concepts and comparison sheet generated!")
