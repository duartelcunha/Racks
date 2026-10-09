// Helpers shared by the render scripts: paths, bundling, progress output and file sizes.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {bundle} from '@remotion/bundler';
import {selectComposition} from '@remotion/renderer';

export const studioDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const outDir = path.join(studioDir, 'out');

export const MB = 1024 * 1024;

/** Bundles the Studio, runs `render(serveUrl)`, then deletes the bundle (about 30 MB in the temp folder). */
export const withBundle = async (render) => {
	process.stdout.write('Bundling... ');
	const serveUrl = await bundle({
		entryPoint: path.join(studioDir, 'src', 'index.ts'),
		rootDir: studioDir,
		publicDir: path.join(studioDir, 'public'),
	});
	console.log('done');
	try {
		await render(serveUrl);
	} finally {
		fs.rmSync(serveUrl, {recursive: true, force: true});
	}
};

export const select = (serveUrl, id, inputProps) => selectComposition({serveUrl, id, inputProps});

/** Single-line progress for a render step, e.g. "  DragIn frames 120/240". */
export const progress = (label) => {
	let last = -1;
	return (done, total) => {
		if (done === last) {
			return;
		}
		last = done;
		process.stdout.write(`\r  ${label} ${done}/${total}${done === total ? '\n' : ''}`);
	};
};

export const describe = (file, composition) => {
	const bytes = fs.statSync(file).size;
	const seconds = (composition.durationInFrames / composition.fps).toFixed(2);
	return {
		bytes,
		line: `${path.relative(studioDir, file)}: ${(bytes / MB).toFixed(2)} MB, ${seconds}s, ${composition.fps} fps, ${composition.durationInFrames} frames`,
	};
};
