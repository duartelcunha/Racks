// Mirrors the inputs the compositions need into public/ and writes src/clips.generated.json.
//
//   docs/media/raw/*.mp4                 -> public/clips/      (the recordings, probed for fps/duration/size)
//   docs/icon.png, docs/screenshots/*.gif -> public/repo/      (brand icon + placeholder footage)
//
// Runs before `studio`, `typecheck` and every render script, and after `npm install`.
// The manifest is only rewritten when its content changes, so the Studio does not reload for nothing.
// A raw clip that cannot be read (say, while Cap is still exporting it) is reported and treated as
// missing, so the other compositions keep working.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseMedia} from '@remotion/media-parser';
import {nodeReader} from '@remotion/media-parser/node';

const studioDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const docsDir = path.resolve(studioDir, '..', '..');
const rawDir = path.join(docsDir, 'media', 'raw');
const clipsDir = path.join(studioDir, 'public', 'clips');
const repoAssetsDir = path.join(studioDir, 'public', 'repo');
const manifestPath = path.join(studioDir, 'src', 'clips.generated.json');

// The clips listed in docs/media/SHOTLIST.md. Keep in sync with `clips` in src/config.ts.
const EXPECTED_CLIPS = ['hero', 'drag-in', 'physics', 'styling', 'finder'];
const REPO_ASSETS = ['icon.png', 'screenshots/demo-1.gif', 'screenshots/demo-2.gif', 'screenshots/demo-3.gif'];

const copyIfChanged = (from, to) => {
	const src = fs.statSync(from);
	const dest = fs.existsSync(to) ? fs.statSync(to) : null;
	if (dest && dest.size === src.size && dest.mtimeMs >= src.mtimeMs) {
		return;
	}
	fs.copyFileSync(from, to);
};

/** Raw recordings as {id, file}. Ids are lowercased: Windows names are case-insensitive, so Hero.mp4 is hero. */
const listRawClips = () => {
	if (!fs.existsSync(rawDir)) {
		return [];
	}
	return fs
		.readdirSync(rawDir)
		.filter((file) => file.toLowerCase().endsWith('.mp4'))
		.map((file) => ({id: path.basename(file, path.extname(file)).toLowerCase(), file}))
		.sort((a, b) => a.id.localeCompare(b.id));
};

const probe = async (file) => {
	// Racks is an individual's open-source project, which Remotion's free license covers.
	const fast = await parseMedia({
		src: file,
		reader: nodeReader,
		acknowledgeRemotionLicense: true,
		fields: {durationInSeconds: true, dimensions: true, fps: true},
	});
	// Some encoders do not store fps or duration in the header; fall back to a full read.
	const needsSlow = fast.fps === null || fast.durationInSeconds === null;
	const slow = needsSlow
		? await parseMedia({
				src: file,
				reader: nodeReader,
				acknowledgeRemotionLicense: true,
				fields: {slowFps: true, slowDurationInSeconds: true},
			})
		: null;
	if (!fast.dimensions) {
		throw new Error('it has no video track');
	}
	return {
		fps: Math.round((fast.fps ?? slow.slowFps) * 1000) / 1000,
		durationInSeconds: Math.round((fast.durationInSeconds ?? slow.slowDurationInSeconds) * 1000) / 1000,
		width: fast.dimensions.width,
		height: fast.dimensions.height,
	};
};

// Logical screen size from the GIF header (bytes 6-9, little-endian).
const gifSize = (file) => {
	const header = Buffer.alloc(10);
	const fd = fs.openSync(file, 'r');
	try {
		fs.readSync(fd, header, 0, 10, 0);
	} finally {
		fs.closeSync(fd);
	}
	if (header.toString('ascii', 0, 3) !== 'GIF') {
		throw new Error(`${file} is not a GIF`);
	}
	return {width: header.readUInt16LE(6), height: header.readUInt16LE(8)};
};

const mirrorRepoAssets = () => {
	fs.mkdirSync(repoAssetsDir, {recursive: true});
	const placeholders = {};
	for (const asset of REPO_ASSETS) {
		const from = path.join(docsDir, asset);
		const to = path.join(repoAssetsDir, path.basename(asset));
		// The old demo GIFs only exist in older checkouts; without them a missing clip has no stand-in.
		if (asset.endsWith('.gif') && !fs.existsSync(from)) continue;
		copyIfChanged(from, to);
		if (asset.endsWith('.gif')) {
			placeholders[path.basename(asset, '.gif')] = {src: `repo/${path.basename(asset)}`, ...gifSize(to)};
		}
	}
	return placeholders;
};

const mirrorClips = async () => {
	fs.mkdirSync(clipsDir, {recursive: true});
	const raw = new Map(listRawClips().map(({id, file}) => [id, file]));
	// Drop copies whose raw file was deleted or renamed.
	for (const file of fs.readdirSync(clipsDir)) {
		if (!raw.has(path.basename(file, path.extname(file)))) {
			fs.rmSync(path.join(clipsDir, file));
		}
	}

	const clips = {};
	for (const id of [...new Set([...EXPECTED_CLIPS, ...raw.keys()])]) {
		if (!raw.has(id)) {
			clips[id] = {present: false};
			continue;
		}
		const target = path.join(clipsDir, `${id}.mp4`);
		copyIfChanged(path.join(rawDir, raw.get(id)), target);
		try {
			clips[id] = {present: true, src: `clips/${id}.mp4`, ...(await probe(target))};
		} catch (error) {
			console.warn(
				`  ! ${raw.get(id)} in docs/media/raw could not be read (${error.message}). Re-export it or remove it; ` +
					'using placeholder footage until then.',
			);
			clips[id] = {present: false};
			continue;
		}
		if (!EXPECTED_CLIPS.includes(id)) {
			console.warn(`  ! ${raw.get(id)} is not in the shot list; it is copied but no composition uses it.`);
		}
	}
	return clips;
};

const writeIfChanged = (file, content) => {
	if (fs.existsSync(file) && fs.readFileSync(file, 'utf8') === content) {
		return false;
	}
	fs.writeFileSync(file, content);
	return true;
};

const placeholders = mirrorRepoAssets();
const clips = await mirrorClips();
const changed = writeIfChanged(manifestPath, `${JSON.stringify({clips, placeholders}, null, '\t')}\n`);

const real = Object.values(clips).filter((clip) => clip.present).length;
if (!changed) {
	console.log(`prepare-clips: up to date (${real} recorded, ${Object.keys(clips).length - real} placeholder)`);
} else {
	console.log('prepare-clips: wrote src/clips.generated.json');
	for (const [id, clip] of Object.entries(clips)) {
		console.log(
			clip.present
				? `  ${id.padEnd(8)} ${clip.width}x${clip.height} @ ${clip.fps} fps, ${clip.durationInSeconds}s`
				: `  ${id.padEnd(8)} missing -> placeholder footage`,
		);
	}
}
