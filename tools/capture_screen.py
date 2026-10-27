import sys
import os
import ctypes
from PIL import Image

def capture_screen(output_path):
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32

    # Switch thread desktop to interactive 'Default' desktop
    h_default = user32.OpenDesktopW('Default', 0, False, 0x01FF)
    if h_default:
        user32.SetThreadDesktop(h_default)

    width = user32.GetSystemMetrics(0)
    height = user32.GetSystemMetrics(1)

    hsrc = user32.GetDC(0)
    hdest = gdi32.CreateCompatibleDC(hsrc)
    hbmp = gdi32.CreateCompatibleBitmap(hsrc, width, height)
    hold = gdi32.SelectObject(hdest, hbmp)
    gdi32.BitBlt(hdest, 0, 0, width, height, hsrc, 0, 0, 0x00CC0020)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [
            ('biSize', ctypes.c_uint32),
            ('biWidth', ctypes.c_int32),
            ('biHeight', ctypes.c_int32),
            ('biPlanes', ctypes.c_uint16),
            ('biBitCount', ctypes.c_uint16),
            ('biCompression', ctypes.c_uint32),
            ('biSizeImage', ctypes.c_uint32),
            ('biXPelsPerMeter', ctypes.c_int32),
            ('biYPelsPerMeter', ctypes.c_int32),
            ('biClrUsed', ctypes.c_uint32),
            ('biClrImportant', ctypes.c_uint32)
        ]

    bmi = BITMAPINFOHEADER()
    bmi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.biWidth = width
    bmi.biHeight = -height  # top-down
    bmi.biPlanes = 1
    bmi.biBitCount = 32
    bmi.biCompression = 0

    buf = ctypes.create_string_buffer(width * height * 4)
    gdi32.GetDIBits(hdest, hbmp, 0, height, buf, ctypes.byref(bmi), 0)

    img = Image.frombuffer('RGBA', (width, height), buf, 'raw', 'BGRA', 0, 1)
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    img.save(output_path)

    gdi32.SelectObject(hdest, hold)
    gdi32.DeleteDC(hdest)
    user32.ReleaseDC(0, hsrc)
    gdi32.DeleteObject(hbmp)
    if h_default:
        user32.CloseDesktop(h_default)

if __name__ == '__main__':
    if len(sys.argv) > 1:
        capture_screen(sys.argv[1])
        print(f"Captured: {sys.argv[1]}")
    else:
        print("Usage: capture_screen.py <output_path>")
