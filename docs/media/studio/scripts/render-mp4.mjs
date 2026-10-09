// Renders the Hero composition (brand intro + hero clip) to out/hero.mp4 (H.264, CRF 18, 30 fps, no audio track).
import fs from 'node:fs';
import path from 'node:path';
import {renderMedia} from '@remotion/renderer';
import {describe, MB, outDir, progress, select, withBundle} from './lib/shared.mjs';

const TARGET_BYTES = 10 * MB;

await withBundle(async (serveUrl) => {
	const composition = await select(serveUrl, 'Hero', {});
	const outputLocation = path.join(outDir, 'hero.mp4');
	fs.mkdirSync(outDir, {recursive: true});

	const report = progress('Hero frames');
	await renderMedia({
		serveUrl,
		composition,
		inputProps: {},
		codec: 'h264',
		crf: 18,
		x264Preset: 'slow',
		pixelFormat: 'yuv420p',
		// Tags the stream as BT.709 limited range, which browsers and GitHub play back with correct colours.
		colorSpace: 'bt709',
		imageFormat: 'jpeg',
		jpegQuality: 95,
		muted: true,
		outputLocation,
		overwrite: true,
		onProgress: ({renderedFrames}) => report(renderedFrames, composition.durationInFrames),
	});

	const {bytes, line} = describe(outputLocation, composition);
	console.log(line);
	if (bytes > TARGET_BYTES) {
		console.warn(`  ! hero.mp4 is over ${TARGET_BYTES / MB} MB; raise crf in scripts/render-mp4.mjs or trim the clip.`);
	}
});
