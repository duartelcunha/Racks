from PIL import Image, ImageDraw

SLATE_BG = (120, 120, 130, 255) # Exact #787882 from user's reference
WHITE = (255, 255, 255, 255)

def make_concept_c_master(size=1024, is_squircle=True):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    pad = size * 0.05
    radius = size * 0.22

    if is_squircle:
        # Beautiful softly rounded squircle badge in slate gray
        draw.rounded_rectangle([pad, pad, size - pad, size - pad], radius=radius, fill=SLATE_BG)
    else:
        draw.rectangle([0, 0, size, size], fill=SLATE_BG)

    # 3 Chunky Solid White Bars
    bar_w = size * 0.62
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

def make_tray_icon(size):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    if size == 16:
        # 16x16: 100% DPI
        draw.rounded_rectangle([1, 1, 14, 14], radius=3, fill=SLATE_BG)
        # 3 bars: 10px wide (x: 3..12), 2px tall, 1px gaps
        for y in [4, 7, 10]:
            draw.rounded_rectangle([3, y, 12, y + 1], radius=1, fill=WHITE)
    elif size == 20:
        # 20x20: 125% DPI
        draw.rounded_rectangle([1, 1, 18, 18], radius=4, fill=SLATE_BG)
        for y in [5, 9, 13]:
            draw.rounded_rectangle([4, y, 15, y + 1], radius=1, fill=WHITE)
    elif size == 24:
        # 24x24: 150% DPI
        draw.rounded_rectangle([1, 1, 22, 22], radius=5, fill=SLATE_BG)
        for y in [5, 10, 15]:
            draw.rounded_rectangle([4, y, 19, y + 2], radius=1, fill=WHITE)
    elif size == 32:
        # 32x32: 200% DPI
        draw.rounded_rectangle([2, 2, 29, 29], radius=6, fill=SLATE_BG)
        for y in [7, 14, 21]:
            draw.rounded_rectangle([6, y, 25, y + 3], radius=2, fill=WHITE)
    return img

if __name__ == "__main__":
    artifact_dir = r"C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
    master = make_concept_c_master(1024, is_squircle=True)
    master.save(f"{artifact_dir}\\scandi_concept_c_master.png")

    # Export to Racks application icons
    master_256 = master.resize((256, 256), Image.Resampling.LANCZOS)
    master_256.save("Racks/ico.png")
    master_256.save("Racks/Icon/ico.png")

    # Multi-resolution ICO: 16, 20, 24, 32, 48, 64, 128, 256
    sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    master.save("Racks/Icon/Ico.ico", format="ICO", sizes=sizes)

    # Export dedicated pixel-tuned tray icons
    t16 = make_tray_icon(16)
    t20 = make_tray_icon(20)
    t24 = make_tray_icon(24)
    t32 = make_tray_icon(32)

    t16.save("Racks/Icon/tray_16.png")
    t20.save("Racks/Icon/tray_20.png")
    t24.save("Racks/Icon/tray_24.png")
    t32.save("Racks/Icon/tray_32.png")
    t16.save("Racks/Icon/tray.png")

    # Preview card
    preview = Image.new("RGBA", (1000, 520), (38, 40, 46, 255))
    p_draw = ImageDraw.Draw(preview)
    p_draw.text((60, 30), "Racks - Concept C: Pure Bold Bars (Scandinavian Flat Silhouette)", fill=(240, 242, 248))
    preview.alpha_composite(master.resize((320, 320), Image.Resampling.LANCZOS), (60, 80))

    tb_dark = Image.new("RGBA", (500, 90), (32, 34, 38, 255))
    tbd_draw = ImageDraw.Draw(tb_dark)
    tbd_draw.text((20, 10), "Dark Windows Taskbar Tray (16, 20, 24, 32, 48, 64px)", fill=(150, 155, 165))
    cx = 20
    for s in [64, 48, 32, 24, 20, 16]:
        sm = master.resize((s, s), Image.Resampling.LANCZOS)
        tb_dark.alpha_composite(sm, (cx, 34 + (64 - s)//2))
        cx += s + 16
    preview.alpha_composite(tb_dark, (440, 120))

    tb_light = Image.new("RGBA", (500, 90), (242, 244, 247, 255))
    tbl_draw = ImageDraw.Draw(tb_light)
    tbl_draw.text((20, 10), "Light Windows Taskbar Tray (16, 20, 24, 32, 48, 64px)", fill=(90, 95, 105))
    cx = 20
    for s in [64, 48, 32, 24, 20, 16]:
        sm = master.resize((s, s), Image.Resampling.LANCZOS)
        tb_light.alpha_composite(sm, (cx, 34 + (64 - s)//2))
        cx += s + 16
    preview.alpha_composite(tb_light, (440, 260))

    preview.save(f"{artifact_dir}\\scandi_concept_c_preview.png")

    print("Concept C assets generated successfully!")
