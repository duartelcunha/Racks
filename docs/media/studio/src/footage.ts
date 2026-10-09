import generated from './clips.generated.json';
import {clips, FALLBACK_SECONDS, MIN_CLIP_SECONDS, type ClipId} from './config';

// Shape written by scripts/prepare-clips.mjs.
type Manifest = {
	clips: Record<
		string,
		| {present: false}
		| {present: true; src: string; fps: number; durationInSeconds: number; width: number; height: number}
	>;
	placeholders: Record<string, {src: string; width: number; height: number}>;
};

const manifest = generated as unknown as Manifest;

/** What a clip slot actually plays: the real recording, or the old demo GIF standing in for it. */
export type Footage = {
	kind: 'video' | 'placeholder';
	src: string;
	width: number;
	height: number;
	/** Frames (at the composition's fps) to skip at the start of the source (videos only). */
	trimBeforeFrames: number;
	durationInFrames: number;
};

/** `fps` is the frame rate of the composition that plays the footage. */
export const resolveFootage = (id: ClipId, fps: number): Footage => {
	const config = clips[id];
	const clip = manifest.clips[id];
	if (clip?.present) {
		const seconds = clip.durationInSeconds - config.trimStartSeconds - config.trimEndSeconds;
		if (seconds < MIN_CLIP_SECONDS) {
			throw new Error(
				`${id}.mp4 is ${clip.durationInSeconds}s; after the trims in config.ts it would be ${seconds.toFixed(2)}s, ` +
					`shorter than the ${MIN_CLIP_SECONDS}s minimum. Re-record it longer or reduce trimStartSeconds/trimEndSeconds.`,
			);
		}
		return {
			kind: 'video',
			src: clip.src,
			width: clip.width,
			height: clip.height,
			trimBeforeFrames: Math.round(config.trimStartSeconds * fps),
			durationInFrames: Math.floor(seconds * fps),
		};
	}
	const placeholder = manifest.placeholders[config.placeholder];
	if (!placeholder) {
		throw new Error(
			`No footage for "${id}": add docs/media/raw/${id}.mp4 (see docs/media/SHOTLIST.md for what to record), then run npm run prepare-clips.`,
		);
	}
	return {
		kind: 'placeholder',
		src: placeholder.src,
		width: placeholder.width,
		height: placeholder.height,
		trimBeforeFrames: 0,
		durationInFrames: FALLBACK_SECONDS * fps,
	};
};
