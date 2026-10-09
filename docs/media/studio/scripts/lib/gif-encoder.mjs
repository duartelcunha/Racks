// Picks the GIF encoder: the gifski CLI when available, otherwise ffmpeg (bundled with Remotion)
// with a two-pass palette and ordered dithering, which is good but bands more on gradients than gifski.
import {spawnSync} from 'node:child_process';
import fs from 'node:fs';
import {createRequire} from 'node:module';
import path from 'node:path';
import {studioDir} from './shared.mjs';

const DEFAULT_GIFSKI = 'C:/Program Files/gifski/gifski.exe';
const require = createRequire(import.meta.url);

/**
 * True for a Windows GUI executable (PE subsystem 2). The gifski desktop installer ships a GUI
 * app named gifski.exe that ignores CLI arguments and opens a window, so it must be skipped.
 */
const isWindowsGuiApp = (file) => {
	if (process.platform !== 'win32') {
		return false;
	}
	const header = Buffer.alloc(1024);
	const fd = fs.openSync(file, 'r');
	try {
		fs.readSync(fd, header, 0, header.length, 0);
	} finally {
		fs.closeSync(fd);
	}
	const pe = header.readUInt32LE(0x3c);
	if (pe + 94 > header.length || header.toString('latin1', pe, pe + 4) !== 'PE\0\0') {
		return false;
	}
	return header.readUInt16LE(pe + 24 + 68) === 2;
};

const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();

/**
 * gifski CLI from $GIFSKI, then PATH, then the default Windows install location. A GIFSKI that
 * does not point at the CLI is an error rather than a silent fallback to the lower-quality encoder.
 */
const findGifski = () => {
	const fromEnv = process.env.GIFSKI;
	if (fromEnv) {
		if (!isFile(fromEnv)) {
			throw new Error(`GIFSKI=${fromEnv} does not exist or is not a file. Point it at the gifski CLI executable.`);
		}
		if (isWindowsGuiApp(fromEnv)) {
			throw new Error(`GIFSKI=${fromEnv} is the gifski desktop app, not the CLI. Use gifski.exe from the release zip.`);
		}
		return fromEnv;
	}
	const exe = process.platform === 'win32' ? 'gifski.exe' : 'gifski';
	const candidates = [
		...(process.env.PATH ?? '').split(path.delimiter).filter(Boolean).map((dir) => path.join(dir, exe)),
		DEFAULT_GIFSKI,
	].map((file) => path.resolve(file));
	for (const file of new Set(candidates)) {
		if (!isFile(file)) {
			continue;
		}
		if (isWindowsGuiApp(file)) {
			console.warn(`  ! ${file} is the gifski desktop app, not the CLI; skipping it.`);
			continue;
		}
		return file;
	}
	return null;
};

const run = (command, args, cwd, what) => {
	const result = spawnSync(command, args, {cwd, stdio: 'inherit'});
	if (result.status !== 0) {
		throw new Error(`${what} failed (exit ${result.status ?? result.error})`);
	}
};

const gifskiEncoder = (gifski) => ({
	name: `gifski (${gifski})`,
	encode: ({framesDir, frames, output, fps, width, quality}) =>
		// Relative frame names (run inside framesDir) keep the command line short on Windows.
		run(
			gifski,
			['--fps', String(fps), '--width', String(width), '--quality', String(quality), '--quiet', '-o', output, ...frames],
			framesDir,
			'gifski',
		),
});

const ffmpegEncoder = () => {
	const cli = path.join(path.dirname(require.resolve('@remotion/cli/package.json')), 'remotion-cli.js');
	return {
		name: 'ffmpeg palette fallback (install the gifski CLI and set GIFSKI for better GIFs)',
		encode: ({framesDir, frames, output, fps, width}) => {
			const digits = /(\d+)\.png$/.exec(frames[0])[1].length;
			const pattern = frames[0].replace(/\d+\.png$/, `%0${digits}d.png`);
			// The palette comes from every pixel (stats_mode=full), so the static background and the
			// small teal accents get their share. Ordered (Bayer) dithering keeps the pattern on a
			// static gradient fixed from frame to frame, so it does not crawl and compresses well.
			const filter = [
				`scale=${width}:-1:flags=lanczos,split[a][b]`,
				'[a]palettegen=max_colors=256:stats_mode=full[p]',
				'[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle',
			].join(';');
			// Run from the studio root (the Remotion CLI warns otherwise) with an absolute input pattern.
			run(
				process.execPath,
				[cli, 'ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-framerate', String(fps), '-start_number', '0', '-i', path.join(framesDir, pattern), '-filter_complex', filter, '-loop', '0', output],
				studioDir,
				'ffmpeg',
			);
		},
	};
};

export const pickGifEncoder = () => {
	const gifski = findGifski();
	return gifski ? gifskiEncoder(gifski) : ffmpegEncoder();
};
