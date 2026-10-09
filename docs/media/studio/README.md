# Racks media studio

A small [Remotion](https://www.remotion.dev) project that turns the raw screen recordings in
`docs/media/raw/` into the README media: a looping hero GIF, a branded hero video and one GIF per
feature section.
It adds the intro, the framing, the captions and the zooms, so the recordings stay plain
(see [`../SHOTLIST.md`](../SHOTLIST.md)).

Without a recording, a composition has no footage and fails with a message naming the missing
file. (Older checkouts that still contain `docs/screenshots/demo-1..3.gif` use those as stand-ins and
show a small "Placeholder footage" tag.) Drop the real clips in and they are picked up on the next
preview or render. No code changes needed.

## Install

Node 20 or newer.

```sh
cd docs/media/studio
npm install
```

`npm install` also runs `prepare-clips` (below), so the Studio works straight away.
The first preview or render downloads Chrome Headless Shell (about 110 MB) once.

## Where the raw clips go

Put the five recordings in `docs/media/raw/` (gitignored) with these exact names:

| File | Composition | Caption |
| --- | --- | --- |
| `hero.mp4` | `HeroLoop` (GIF), `Hero` (MP4 with the brand intro) | (none) |
| `drag-in.mp4` | `DragIn` | Drag in. Done. |
| `physics.mp4` | `Physics` | Physics that feel real |
| `styling.mp4` | `Styling` | A rack for every mood |
| `finder.mp4` | `Finder` | Find anything instantly |

File names are matched case-insensitively (`Hero.mp4` counts as `hero.mp4`). Record with Cap's
auto-zoom **off**: the zooms come from `config.ts`, and two cameras would fight.

`npm run prepare-clips` copies them into `public/clips/`, probes each one (fps, duration, size)
and writes `src/clips.generated.json`. It runs automatically before `studio`, `typecheck` and
every render command. It only rewrites the manifest when something changed (so the Studio
doesn't reload for nothing), and only then lists every clip; otherwise it prints a one-line
summary. A missing clip is marked absent and its placeholder GIF is used instead. A clip that
cannot be read (half-exported, damaged) gets a warning naming the file and is treated as missing,
so the other compositions keep working.

## Preview

```sh
npm run studio        # same as: npx remotion studio (runs prepare-clips first)
```

If you call `npx remotion studio` directly after adding new clips, run `npm run prepare-clips`
first so the manifest sees them.

## Retiming after recording

Everything you tune lives in **`src/config.ts`**:

- `brand`: wordmark, tagline and the placeholder label.
- `clips.<id>.caption`: the lower-third text (`null` hides it).
- `clips.<id>.trimStartSeconds` / `trimEndSeconds`: cut dead time from the raw recording. The
  composition length follows the trimmed clip automatically (at least 1 s must remain).
- `clips.<id>.zoom`: keyframes `{frame, scale, x, y}`. `scale: 1` shows the whole clip;
  `x`/`y` (0 to 1) is the point that stays fixed while zooming, e.g. `{x: 0.2, y: 0.8}` zooms
  towards the bottom-left. Values ease between keyframes and hold before the first and after
  the last. Keep it to one push-in, hold and release per clip (scale 1.15 to 1.25, ramps of
  36+ frames, a hold of 45+ frames); an empty array means no zoom. A zoom changes every pixel,
  which makes it the most expensive thing in a GIF.
- `timing`: intro and tagline length, dock speed, caption delay and the loop fades.

**Every frame number in `config.ts` is a 30-fps frame** counted from the start of the trimmed
clip. That holds for 60-fps recordings and for the GIFs, which render at 25 fps (the compositions
convert). The Studio previews at 30 fps, so its frame counter matches `config.ts`. Scrub the
composition to find frame numbers, edit `config.ts`, and the preview updates live. To preview at
the GIF rate, set the `fps` prop to 25 in the Studio's props panel.

## Render

```sh
npm run render:mp4    # out/hero.mp4  (Hero: H.264, CRF 18, 1920x1080, 30 fps, no audio; warns over 10 MB)
npm run render:gifs   # out/hero.gif (HeroLoop) + out/drag-in.gif, physics.gif, styling.gif, finder.gif
npm run render:all    # both
```

`render:gifs` renders each loop at **25 fps** (GIF frame delays come in 1/100 s steps, so 30 fps
would alternate 30 and 40 ms and judder) as PNG frames into `out/frames/<name>/`: the hero at
1280 px wide, because the README shows it full width, and the sections at 960 px. It then
encodes them with [gifski](https://gif.ski) (`--fps 25 --width <w> --quality 90`) and warns
about a section GIF over 3 MB or a hero GIF over 5 MB.

gifski is looked up in this order: the `GIFSKI` environment variable, `PATH`, then
`C:/Program Files/gifski/gifski.exe`. It must be the **command-line** gifski (the release zip
from gif.ski or GitHub). The gifski desktop installer puts a GUI app at that default path; the
script detects it and skips it. If `GIFSKI` is set but is missing, a folder or the GUI app, the
script stops with an error instead of quietly using another encoder. Without any gifski CLI it
falls back to the ffmpeg that ships with Remotion (full-frame palette, ordered dithering): fine
for drafts, a little more banding on gradients.

```sh
GIFSKI="C:/tools/gifski/win/gifski.exe" npm run render:gifs                # Git Bash
```

```powershell
$env:GIFSKI = "C:\tools\gifski\win\gifski.exe"; npm run render:gifs    # PowerShell
```

Each render bundles the project into the temp folder and deletes the bundle when it finishes.

## Outputs

| Path | What |
| --- | --- |
| `out/hero.gif` | README hero: the hero clip in its frame, without the lockup (the README header already shows it); fades in and out so it loops cleanly |
| `out/hero.mp4` | Branded hero: the lockup docks above the clip; loops seamlessly (first and last frame are the bare background). For releases, social posts or a `<video>` embed |
| `out/<section>.gif` | One per README section, each fades in and out so it loops cleanly |
| `out/frames/` | Intermediate PNG frames; safe to delete |

`out/`, `node_modules/`, `public/clips/`, `public/repo/` and `src/clips.generated.json` are
gitignored. Copy the finished files to wherever the README references them.

## Layout

```
scripts/prepare-clips.mjs      mirror raw clips + docs assets into public/, write the manifest
scripts/render-mp4.mjs         Hero -> out/hero.mp4
scripts/render-gifs.mjs        HeroLoop + sections -> PNG frames -> GIFs
scripts/lib/                   bundling/progress helpers, GIF encoder selection
src/config.ts                  copy, captions, trims, zoom keyframes, timing
src/theme.ts                   colours, font, frame style, springs
src/footage.ts                 real clip or placeholder GIF for a slot, and its length
src/lib/                       time base (30-fps config frames at any fps), zoom, fit
src/components/                Background, Intro, Clip (frame + media + caption + tag)
src/compositions/              Hero (MP4) and Loop (HeroLoop and the section GIFs)
```

Fonts: the compositions use Segoe UI Variable, which ships with Windows 11. Rendering on
another OS falls back to the system UI font.
