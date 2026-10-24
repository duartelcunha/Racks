from PIL import Image, ImageDraw, ImageFilter
import random

def make_chassis(size=1024):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pad = size * 0.05
    radius = size * 0.22
    box = [pad, pad, size - pad, size - pad]

    # Chassis shadow
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    s_draw.rounded_rectangle([pad + 4, pad + 12, size - pad - 4, size - pad + 20], 
                             radius=radius, fill=(0, 0, 0, 160))
    shadow = shadow.filter(ImageFilter.GaussianBlur(size * 0.035))
    canvas.alpha_composite(shadow)

    # Dark Obsidian / Ceramic Chassis
    bg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    b_draw = ImageDraw.Draw(bg)
    b_draw.rounded_rectangle(box, radius=radius, fill=(18, 20, 26, 255))
    # Outer crisp rim catch
    b_draw.rounded_rectangle(box, radius=radius, outline=(255, 255, 255, 40), width=int(size * 0.012))
    # Inner rim shadow
    b_draw.rounded_rectangle([pad + 4, pad + 4, size - pad - 4, size - pad - 4], radius=radius - 4, outline=(0, 0, 0, 90), width=int(size * 0.008))
    canvas.alpha_composite(bg)
    return canvas

def add_frosted_grain(canvas, size=1024):
    pad = size * 0.05
    noise_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    random.seed(1337)
    pixels = noise_layer.load()
    for py in range(int(pad), int(size - pad), 2):
        for px in range(int(pad), int(size - pad), 2):
            if random.random() < 0.22:
                v = random.randint(180, 255)
                a = random.randint(5, 12)
                pixels[px, py] = (v, v, v, a)
                pixels[px + 1, py] = (v, v, v, a)
                pixels[px, py + 1] = (v, v, v, a)
                pixels[px + 1, py + 1] = (v, v, v, a)
    canvas.alpha_composite(noise_layer)
    return canvas

# Option A: Frosted Modular Slabs (With etched slots)
def make_option_a(size=1024):
    canvas = make_chassis(size)
    pad = size * 0.05
    slab_w = size * 0.64
    slab_h = size * 0.13
    slab_r = size * 0.035
    cx = size / 2.0
    left = cx - slab_w / 2.0
    right = cx + slab_w / 2.0
    y_spacing = size * 0.20
    cy_mid = size / 2.0
    y_mids = [cy_mid - y_spacing, cy_mid, cy_mid + y_spacing]

    for idx, ym in enumerate(y_mids):
        y = ym - slab_h / 2.0
        is_center = (idx == 1)

        # Drop shadow
        slab_shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ss_draw = ImageDraw.Draw(slab_shadow)
        ss_draw.rounded_rectangle([left, y + size * 0.015, right, y + slab_h + size * 0.02], 
                                  radius=slab_r, fill=(0, 0, 0, 200 if is_center else 140))
        slab_shadow = slab_shadow.filter(ImageFilter.GaussianBlur(size * 0.02))
        canvas.alpha_composite(slab_shadow)

        slab = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        sl_draw = ImageDraw.Draw(slab)

        body_fill = (255, 255, 255, 80 if is_center else 35)
        rim_color = (255, 255, 255, 180 if is_center else 85)
        specular = (255, 255, 255, 240 if is_center else 150)
        slot_fill = (255, 255, 255, 110 if is_center else 45)

        sl_draw.rounded_rectangle([left, y, right, y + slab_h], radius=slab_r, fill=body_fill)
        sl_draw.rounded_rectangle([left, y, right, y + slab_h], radius=slab_r, outline=rim_color, width=int(size * 0.008))

        # Top specular catch
        spec_h = int(size * 0.006)
        sl_draw.rounded_rectangle([left + slab_r * 0.5, y + 2, right - slab_r * 0.5, y + 2 + spec_h], 
                                  radius=spec_h, fill=specular)

        # 3 slots
        slot_w = slab_w * 0.22
        slot_h = slab_h * 0.46
        slot_r = size * 0.015
        sy = y + (slab_h - slot_h) / 2.0
        gap = (slab_w - (3 * slot_w)) / 4.0
        for i in range(3):
            sx = left + gap + i * (slot_w + gap)
            sl_draw.rounded_rectangle([sx, sy, sx + slot_w, sy + slot_h], radius=slot_r, fill=slot_fill)

        canvas.alpha_composite(slab)

    return add_frosted_grain(canvas, size)

# Option B: Pure Frosted Glass Pills (Minimal, clean, high contrast)
def make_option_b(size=1024):
    canvas = make_chassis(size)
    bar_w = size * 0.62
    bar_h = size * 0.125
    bar_r = bar_h / 2.0
    cx = size / 2.0
    left = cx - bar_w / 2.0
    right = cx + bar_w / 2.0
    y_spacing = size * 0.20
    cy_mid = size / 2.0
    y_mids = [cy_mid - y_spacing, cy_mid, cy_mid + y_spacing]

    for idx, ym in enumerate(y_mids):
        y = ym - bar_h / 2.0
        is_center = (idx == 1)

        # Drop shadow for depth
        pill_shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ps_draw = ImageDraw.Draw(pill_shadow)
        ps_draw.rounded_rectangle([left, y + size * 0.018, right, y + bar_h + size * 0.022], 
                                  radius=bar_r, fill=(0, 0, 0, 210 if is_center else 140))
        pill_shadow = pill_shadow.filter(ImageFilter.GaussianBlur(size * 0.025))
        canvas.alpha_composite(pill_shadow)

        pill = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        p_draw = ImageDraw.Draw(pill)

        # Center pill is radiant milky white frosted glass; top/bottom are smoked frosted glass
        if is_center:
            body_fill = (250, 252, 255, 230)
            rim_color = (255, 255, 255, 255)
            specular = (255, 255, 255, 255)
        else:
            body_fill = (160, 172, 190, 70)
            rim_color = (210, 220, 235, 110)
            specular = (255, 255, 255, 160)

        p_draw.rounded_rectangle([left, y, right, y + bar_h], radius=bar_r, fill=body_fill)
        p_draw.rounded_rectangle([left, y, right, y + bar_h], radius=bar_r, outline=rim_color, width=int(size * 0.010))

        # Top edge specular catch
        spec_h = int(size * 0.007)
        p_draw.rounded_rectangle([left + bar_r * 0.6, y + 2, right - bar_r * 0.6, y + 2 + spec_h], 
                                 radius=spec_h, fill=specular)

        canvas.alpha_composite(pill)

    return add_frosted_grain(canvas, size)

if __name__ == "__main__":
    artifact_dir = r"C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
    oa = make_option_a(1024)
    ob = make_option_b(1024)

    oa.save(f"{artifact_dir}\\frosted_option_a.png")
    ob.save(f"{artifact_dir}\\frosted_option_b.png")

    # Side by side sheet with small sizes
    sheet = Image.new("RGBA", (1100, 500), (20, 22, 28, 255))
    sheet.alpha_composite(oa.resize((320, 320), Image.Resampling.LANCZOS), (80, 40))
    sheet.alpha_composite(ob.resize((320, 320), Image.Resampling.LANCZOS), (620, 40))

    # Add size previews at bottom
    x_a = 80
    x_b = 620
    for s in [64, 48, 32, 24, 16]:
        ra = oa.resize((s, s), Image.Resampling.LANCZOS)
        rb = ob.resize((s, s), Image.Resampling.LANCZOS)
        sheet.alpha_composite(ra, (x_a, 400 + (64 - s)//2))
        sheet.alpha_composite(rb, (x_b, 400 + (64 - s)//2))
        x_a += s + 18
        x_b += s + 18

    sheet.save(f"{artifact_dir}\\frosted_comparison.png")
    print("Comparison saved!")
