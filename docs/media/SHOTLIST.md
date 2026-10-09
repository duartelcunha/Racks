# README shot list

> **Status.** The five clips in the README were captured with scripted mouse and keyboard input on a
> 1920x1080 monitor (ffmpeg screen capture, one demo-file set, no personal files on screen), not with Cap.
> The scripts are tied to one machine and are not part of the repo. This list stays as the brief for
> re-recording by hand. Two deviations: `styling` is the rack settings panel (right-click, Settings...)
> with live colour changes, and `finder` shows Quick Finder only, because Magic Organize works on the
> real Desktop folder and can't be recorded safely on a working PC.

Five short clips for the README. Each is edited afterwards in `docs/media/studio` (Remotion), which
adds the intro, captions, framing and zooms, so **record plain**: no text, no editing.

## Before you record

**Tool:** [Cap](https://cap.so), the Windows build, in Studio mode.

| Setting | Value |
| --- | --- |
| Resolution | 1920×1080 (set the display to 100% scale for the session) |
| Frame rate | 60 fps |
| Cursor | Cap "smooth cursor" on, size 150% |
| Auto-zoom | **Off**: Remotion adds the zooms (two cameras would fight) |
| Background / padding | **Off**: Remotion adds the frame |
| Export | MP4, highest quality |

**Desktop prep:**
- One calm wallpaper for all 5 clips (no fog photo: fine noise dithers badly in GIFs). A soft gradient or a blurred landscape works best.
- Hide taskbar clutter: no notifications, close the other tray apps, turn on Focus mode.
- Use **demo files only**, with neutral names like `Invoice-March.pdf`, `Holiday.jpg`, `Notes.txt`, `Budget.xlsx`, `Logo.png`, `Report.docx`. Nothing personal on screen.
- Reset Racks: tray → *Export* your layout first, then start from an empty layout.
- Move slowly and deliberately. Pause ~0.5 s before and after each action.

Save the files to `docs/media/raw/` with the exact names below (that folder is gitignored).

---

## 1. `hero.mp4`: "clutter → clean" (6–8 s)
The one clip that sells the app. It loops at the top of the README.

**Setup:** ~12 demo files scattered across the desktop. Two empty racks: **Work** (blue) and **Personal** (teal).
**Action:**
1. Hold 0.5 s on the messy desktop.
2. Box-select 4–5 work files → drag into **Work**.
3. Box-select the rest → drag into **Personal**.
4. Hold 1 s on the clean wallpaper with the two tidy racks.

## 2. `drag-in.mp4`: drop modes (6–8 s)
**Setup:** one rack with 2 items; 3 files on the desktop.
**Action:**
1. Plain drop of a file → it disappears from the desktop (moved).
2. Hold **Ctrl** and drop a file → the shortcut appears, the original stays on the desktop.
3. Drag an item out of the rack back to the desktop → a single file, no duplicate.

## 3. `physics.mp4`: puck on ice (6–8 s)
**Setup:** three racks spread across the screen. Lock one with right-click → *Lock rack* **before** recording.
**Action:**
1. Grab a rack, flick it fast into a second one, release while moving → it glides; the second gets pushed and bounces off the screen edge.
2. Flick a rack into the **locked** one → it stops dead and the locked rack doesn't move.
3. Hold 0.5 s once everything settles.

> Do 3–4 takes. Pick the one where the chain reaction is clearest.

## 4. `styling.mp4`: live styling (7–8 s)
**Setup:** one rack with ~6 items, settings panel closed.
**Action:**
1. Open the rack's settings: the panel snaps beside it.
2. Click the themes one after another: **Glass** → **Neon** → **Solarized Light** (each holds ~0.7 s).
3. Drag the opacity slider down to show the wallpaper through it.
4. Change the title colour in the hex picker.
5. Collapse the rack with the chevron, then expand it.

## 5. `finder.mp4`: Quick Finder + Magic Organize (8 s)
**Setup:** 3 racks with demo files in them; ~8 loose files on the desktop.
**Action:**
1. Press **Ctrl+Shift+Space** and type `inv` → the result `Invoice-March.pdf` appears → press Enter to open it (close it right after).
2. Tray → **✨ Magic Organize Desktop…** → preview of the groups → confirm → the racks appear.

---

## Checklist before handing over
- [ ] 5 files in `docs/media/raw/`, named as above
- [ ] 1920×1080, 60 fps, 6–8 s each (trim the dead time at the start and end)
- [ ] No personal names, notifications or other apps visible
