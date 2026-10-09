// Renders the framed loops to out/<name>.gif: PNG frames at GIF size into out/frames/<name>/,
// then gifski (or the ffmpeg fallback) encodes them.
import fs from 'node:fs';
import path from 'node:path';
import {renderFrames} from '@remotion/renderer';
import {pickGifEncoder} from './lib/gif-encoder.mjs';
import {describe, MB, outDir, progress, select, withBundle} from './lib/shared.mjs';

// GIF frame delays come in 1/100 s steps: 25 fps is an even 40 ms, 30 fps would alternate 30/40 ms and judder.
const GIF_FPS = 25;
const GIF_QUALITY = 80;

// Composition id -> output name, width and size budget. The hero is shown full width in the
// README, so it gets more pixels (and a bigger budget) than the section GIFs.
const JOBS = [
	{id: 'HeroLoop', name: 'hero', width: 1280, warnBytes: 5 * MB},
	{id: 'DragIn', name: 'drag-in', width: 960, warnBytes: 3 * MB},
	{id: 'Physics', name: 'physics', width: 960, warnBytes: 3 * MB},
	{id: 'Styling', name: 'styling', width: 960, warnBytes: 3 * MB},
	{id: 'Finder', name: 'finder', width: 960, warnBytes: 3 * MB},
];

const encoder = pickGifEncoder();
console.log(`GIF encoder: ${encoder.name}`);
const inputProps = {fps: GIF_FPS};

await withBundle(async (serveUrl) => {
	for (const {id, name, width, warnBytes} of JOBS) {
		const composition = await select(serveUrl, id, inputProps);
		const framesDir = path.join(outDir, 'frames', name);
		fs.rmSync(framesDir, {recursive: true, force: true});
		fs.mkdirSync(framesDir, {recursive: true});

		const report = progress(`${id} frames`);
		await renderFrames({
			serveUrl,
			composition,
			inputProps,
			outputDir: framesDir,
			imageFormat: 'png',
			imageSequencePattern: 'frame-[frame].[ext]',
			scale: width / composition.width,
			onFrameUpdate: (rendered) => report(rendered, composition.durationInFrames),
		});

		const output = path.join(outDir, `${name}.gif`);
		const frames = fs.readdirSync(framesDir).filter((file) => file.endsWith('.png')).sort();
		encoder.encode({framesDir, frames, output, fps: composition.fps, width, quality: GIF_QUALITY});

		const {bytes, line} = describe(output, composition);
		console.log(`  ${line}`);
		if (bytes > warnBytes) {
			console.warn(`  ! ${name}.gif is over ${warnBytes / MB} MB; trim the clip, shorten its zoom, or lower GIF_QUALITY (gifski only).`);
		}
	}
});
