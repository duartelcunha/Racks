import math
import random
import os
from PIL import Image, ImageDraw, ImageFilter

def make_pro_frosted_mark(size=1024):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pad = size * 0.055
    radius = size * 0.22
    box = [pad, pad, size - pad, size - pad]

    # --- 1. Chassis Cast Shadow (Subtle studio floor shadow) ---
    shadow_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow_layer)
    # Contact shadow
    s_draw.rounded_rectangle([pad + size * 0.005, pad + size * 0.015, size - pad - size * 0.005, size - pad + size * 0.025],
                             radius=radius, fill=(0, 0, 0, 160))
    # Ambient diffuse shadow
    s_draw.rounded_rectangle([pad + size * 0.01, pad + size * 0.025, size - pad - size * 0.01, size - pad + size * 0.045],
                             radius=radius, fill=(0, 0, 0, 100))
    shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(size * 0.035))
    canvas.alpha_composite(shadow_layer)

    # --- 2. Obsidian / Smoked Quartz Chassis ---
    chassis = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    c_draw = ImageDraw.Draw(chassis)

    # Base dark graphite/obsidian body
    c_draw.rounded_rectangle(box, radius=radius, fill=(17, 19, 24, 255))

    # Subtle vertical lighting gradient (lighter at top, deeper at bottom)
    grad_overlay = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(grad_overlay)
    for y in range(int(pad), int(size - pad)):
        t = (y - pad) / (size - 2 * pad)
        # 26 at top down to 14 at bottom
        val = int(24 * (1.0 - t * 0.5))
        g_draw.line([(pad, y), (size - pad, y)], fill=(255, 255, 255, val))

    # Mask gradient to chassis shape
    chassis_mask = Image.new("L", (size, size), 0)
    cm_draw = ImageDraw.Draw(chassis_mask)
    cm_draw.rounded_rectangle(box, radius=radius, fill=255)
    chassis.alpha_composite(grad_overlay)

    # Precision CNC Chamfered Outer Rim: Top catches directional key light, bottom recedes
    rim_w = int(max(2, size * 0.011))
    c_draw.rounded_rectangle(box, radius=radius, outline=(255, 255, 255, 42), width=rim_w)
    
    # Top specular rim line
    top_rim_h = int(size * 0.005)
    c_draw.rounded_rectangle([pad + radius * 0.6, pad + 1, size - pad - radius * 0.6, pad + 1 + top_rim_h],
                             radius=top_rim_h, fill=(255, 255, 255, 90))

    # Inner recessed ambient occlusion border
    inner_pad = pad + rim_w
    inner_box = [inner_pad, inner_pad, size - inner_pad, size - inner_pad]
    c_draw.rounded_rectangle(inner_box, radius=radius - rim_w, outline=(0, 0, 0, 110), width=int(size * 0.007))

    canvas.alpha_composite(chassis)

    # --- 3. Floating Modular Frosted Glass Shelves ---
    shelf_w = size * 0.65
    shelf_h = size * 0.125
    shelf_r = shelf_h / 2.0  # Pill curvature
    cx = size / 2.0
    left = cx - shelf_w / 2.0
    right = cx + shelf_w / 2.0
    
    y_gap = size * 0.195
    cy_mid = size / 2.0
    y_mids = [cy_mid - y_gap, cy_mid, cy_mid + y_gap]

    for idx, ym in enumerate(y_mids):
        y = ym - shelf_h / 2.0
        is_center = (idx == 1)

        # 3A. Ambient Occlusion & Cast Drop Shadow
        drop_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        d_draw = ImageDraw.Draw(drop_layer)
        
        # Dual-layer shadow: sharp contact shadow + broad ambient shadow
        if is_center:
            # Center active shelf has greater optical elevation
            d_draw.rounded_rectangle([left + size * 0.005, y + size * 0.012, right - size * 0.005, y + shelf_h + size * 0.018],
                                     radius=shelf_r, fill=(0, 0, 0, 230))
            d_draw.rounded_rectangle([left + size * 0.01, y + size * 0.024, right - size * 0.01, y + shelf_h + size * 0.038],
                                     radius=shelf_r, fill=(0, 0, 0, 150))
            drop_blur = size * 0.024
        else:
            d_draw.rounded_rectangle([left + size * 0.004, y + size * 0.008, right - size * 0.004, y + shelf_h + size * 0.014],
                                     radius=shelf_r, fill=(0, 0, 0, 170))
            d_draw.rounded_rectangle([left + size * 0.008, y + size * 0.016, right - size * 0.008, y + shelf_h + size * 0.026],
                                     radius=shelf_r, fill=(0, 0, 0, 95))
            drop_blur = size * 0.018

        drop_layer = drop_layer.filter(ImageFilter.GaussianBlur(drop_blur))
        canvas.alpha_composite(drop_layer)

        # 3B. Shelf Body Glass & Bevel Structure
        shelf_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        sl_draw = ImageDraw.Draw(shelf_layer)

        if is_center:
            # Luminous Milky-White Frosted Silica Glass
            # Gradient fill from top to bottom
            for sy in range(int(y), int(y + shelf_h)):
                progress = (sy - y) / shelf_h
                # Light cream/milky white top down to dense frosted body
                alpha = int(245 - 20 * progress)
                val = int(255 - 8 * progress)
                sl_draw.line([(left, sy), (right, sy)], fill=(val, val, val, alpha))
            
            # Mask to rounded pill
            mask_pill = Image.new("L", (size, size), 0)
            mp_draw = ImageDraw.Draw(mask_pill)
            mp_draw.rounded_rectangle([left, y, right, y + shelf_h], radius=shelf_r, fill=255)
            
            shelf_masked = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            shelf_masked.paste(shelf_layer, mask=mask_pill)
            shelf_layer = shelf_masked
            sl_draw = ImageDraw.Draw(shelf_layer)

            # Outer Chamfer Rim: Pure crisp white catch
            sl_draw.rounded_rectangle([left, y, right, y + shelf_h], radius=shelf_r, 
                                     outline=(255, 255, 255, 255), width=int(max(2, size * 0.009)))
            
            # Top Specular Catch (Micro-chamfer reflection)
            spec_h = int(size * 0.007)
            sl_draw.rounded_rectangle([left + shelf_r * 0.5, y + 2, right - shelf_r * 0.5, y + 2 + spec_h],
                                     radius=spec_h, fill=(255, 255, 255, 255))
            
            # Bottom reflected edge catch (Subtle bounce)
            bounce_h = int(size * 0.004)
            sl_draw.rounded_rectangle([left + shelf_r * 0.7, y + shelf_h - 2 - bounce_h, right - shelf_r * 0.7, y + shelf_h - 2],
                                     radius=bounce_h, fill=(200, 210, 225, 120))
        else:
            # Secondary Shelves: Smoked Frosted Glass
            for sy in range(int(y), int(y + shelf_h)):
                progress = (sy - y) / shelf_h
                alpha = int(75 - 15 * progress)
                r_v = int(175 - 10 * progress)
                g_v = int(185 - 10 * progress)
                b_v = int(200 - 10 * progress)
                sl_draw.line([(left, sy), (right, sy)], fill=(r_v, g_v, b_v, alpha))

            mask_pill = Image.new("L", (size, size), 0)
            mp_draw = ImageDraw.Draw(mask_pill)
            mp_draw.rounded_rectangle([left, y, right, y + shelf_h], radius=shelf_r, fill=255)

            shelf_masked = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            shelf_masked.paste(shelf_layer, mask=mask_pill)
            shelf_layer = shelf_masked
            sl_draw = ImageDraw.Draw(shelf_layer)

            # Frosted glass rim definition
            sl_draw.rounded_rectangle([left, y, right, y + shelf_h], radius=shelf_r, 
                                     outline=(220, 230, 245, 120), width=int(max(2, size * 0.008)))
            
            # Top specular catch
            spec_h = int(size * 0.006)
            sl_draw.rounded_rectangle([left + shelf_r * 0.5, y + 2, right - shelf_r * 0.5, y + 2 + spec_h],
                                     radius=spec_h, fill=(255, 255, 255, 160))

        canvas.alpha_composite(shelf_layer)

    # --- 4. Authentic Sandblasted Micro-Texture (Frosted Grain) ---
    grain_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    random.seed(4242)
    pixels = grain_layer.load()
    step = 2
    for py in range(int(pad), int(size - pad), step):
        for px in range(int(pad), int(size - pad), step):
            if random.random() < 0.28:
                v = random.randint(210, 255)
                # Delicate micro-alpha (3-8 out of 255)
                a = random.randint(3, 8)
                for dy in range(step):
                    for dx in range(step):
                        if px + dx < size and py + dy < size:
                            pixels[px + dx, py + dy] = (v, v, v, a)

    # Apply texture only within chassis mask
    canvas.alpha_composite(grain_layer)
    return canvas

if __name__ == "__main__":
    artifact_dir = r"C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
    os.makedirs(artifact_dir, exist_ok=True)
    master = make_pro_frosted_mark(1024)
    master.save(os.path.join(artifact_dir, "pro_frosted_master.png"))

    # Multi-resolution preview card
    preview = Image.new("RGBA", (1000, 480), (16, 18, 22, 255))
    preview.alpha_composite(master.resize((320, 320), Image.Resampling.LANCZOS), (60, 40))

    # Dark background small size row
    curr_x = 440
    for s in [64, 48, 32, 24, 16]:
        sm = master.resize((s, s), Image.Resampling.LANCZOS)
        preview.alpha_composite(sm, (curr_x, 100 + (64 - s)//2))
        curr_x += s + 24

    # Light background bar for contrast validation
    light_bar = Image.new("RGBA", (500, 120), (238, 240, 245, 255))
    curr_x = 20
    for s in [64, 48, 32, 24, 16]:
        sm = master.resize((s, s), Image.Resampling.LANCZOS)
        light_bar.alpha_composite(sm, (curr_x, 28 + (64 - s)//2))
        curr_x += s + 24
    preview.alpha_composite(light_bar, (440, 240))

    preview.save(os.path.join(artifact_dir, "pro_frosted_preview.png"))
    print("Master and preview rendered successfully!")
