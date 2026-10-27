from PIL import Image, ImageDraw, ImageFilter
import random

def create_frosted_neutral_mark(size=1024):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pad = size * 0.05
    radius = size * 0.22
    box = [pad, pad, size - pad, size - pad]

    # 1. Subtle soft chassis drop shadow
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    s_draw.rounded_rectangle([pad + 4, pad + 12, size - pad - 4, size - pad + 20], 
                             radius=radius, fill=(0, 0, 0, 160))
    shadow = shadow.filter(ImageFilter.GaussianBlur(size * 0.035))
    canvas.alpha_composite(shadow)

    # 2. Dark Obsidian / Titanium Glass Chassis Background
    # Subtle gradient from deep graphite (top) to pitch black (bottom)
    bg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    b_draw = ImageDraw.Draw(bg)
    b_draw.rounded_rectangle(box, radius=radius, fill=(20, 22, 28, 255))
    
    # Outer Bevel Rim: Top edge catch (light gray), bottom edge (darker shadow)
    b_draw.rounded_rectangle(box, radius=radius, outline=(255, 255, 255, 35), width=int(size * 0.012))
    # Inner chamfer
    inner_box = [pad + 4, pad + 4, size - pad - 4, size - pad - 4]
    b_draw.rounded_rectangle(inner_box, radius=radius - 4, outline=(0, 0, 0, 80), width=int(size * 0.008))
    canvas.alpha_composite(bg)

    # 3. 3 Tiered Frosted Glass Slabs (With physical depth, edge specular, and ambient occlusion)
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

        # Ambient Occlusion / Drop Shadow behind each slab
        slab_shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ss_draw = ImageDraw.Draw(slab_shadow)
        ss_draw.rounded_rectangle([left, y + size * 0.015, right, y + slab_h + size * 0.02], 
                                  radius=slab_r, fill=(0, 0, 0, 180 if is_center else 140))
        slab_shadow = slab_shadow.filter(ImageFilter.GaussianBlur(size * 0.02))
        canvas.alpha_composite(slab_shadow)

        # Slab Glass Body (Translucent Frosted Glass)
        slab_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        sl_draw = ImageDraw.Draw(slab_layer)

        # Center rack is the prominent active frosted rack
        if is_center:
            body_fill = (245, 248, 252, 60) # Brighter frosted glass
            rim_color = (255, 255, 255, 160)
            specular_top = (255, 255, 255, 230)
            slot_fill = (255, 255, 255, 80)
        else:
            body_fill = (220, 226, 235, 30) # Soft muted frosted glass
            rim_color = (255, 255, 255, 75)
            specular_top = (255, 255, 255, 140)
            slot_fill = (255, 255, 255, 40)

        # Slab base
        sl_draw.rounded_rectangle([left, y, right, y + slab_h], radius=slab_r, fill=body_fill)
        # Slab chamfer outline
        sl_draw.rounded_rectangle([left, y, right, y + slab_h], radius=slab_r, outline=rim_color, width=int(size * 0.008))

        # Top edge specular light reflection (gives authentic glass thickness)
        spec_h = int(size * 0.006)
        sl_draw.rounded_rectangle([left + slab_r * 0.5, y + 2, right - slab_r * 0.5, y + 2 + spec_h], 
                                  radius=spec_h, fill=specular_top)

        # 3 Subtle frosted tile/file slots etched into the glass
        slot_w = slab_w * 0.22
        slot_h = slab_h * 0.46
        slot_r = size * 0.015
        sy = y + (slab_h - slot_h) / 2.0
        gap = (slab_w - (3 * slot_w)) / 4.0

        for i in range(3):
            sx = left + gap + i * (slot_w + gap)
            # Etched groove
            sl_draw.rounded_rectangle([sx, sy, sx + slot_w, sy + slot_h], radius=slot_r, fill=slot_fill)
            # Subtle top groove catch
            sl_draw.line([(sx + 3, sy + 1), (sx + slot_w - 3, sy + 1)], fill=(255, 255, 255, 100), width=int(size * 0.003))

        canvas.alpha_composite(slab_layer)

    # 4. Subtle Frosted Glass Grain / Micro-Texture Overlay
    noise_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    random.seed(42) # Deterministic grain
    pixels = noise_layer.load()
    for py in range(int(pad), int(size - pad), 2):
        for px in range(int(pad), int(size - pad), 2):
            if random.random() < 0.25:
                v = random.randint(180, 255)
                a = random.randint(6, 14) # Very subtle 3-5% opacity grain
                pixels[px, py] = (v, v, v, a)
                pixels[px + 1, py] = (v, v, v, a)
                pixels[px, py + 1] = (v, v, v, a)
                pixels[px + 1, py + 1] = (v, v, v, a)
    canvas.alpha_composite(noise_layer)

    return canvas

if __name__ == "__main__":
    artifact_dir = r"C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
    icon = create_frosted_neutral_mark(1024)
    icon.save(f"{artifact_dir}\\frosted_neutral_test.png")
    print("Frosted neutral mark generated!")
