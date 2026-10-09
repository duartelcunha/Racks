// Everything you retime after recording lives here: copy, captions, trims and zoom keyframes.
//
// Every frame number in this file is a 30-fps frame (FPS), counted from the first frame of the
// clip after `trimStartSeconds`. That holds for 60-fps recordings and for the GIFs, which render
// at 25 fps (GIF delays come in 1/100 s steps, so 30 fps would judder): the compositions convert.

export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
// Length used for a clip when its raw recording is missing and placeholder footage stands in.
export const FALLBACK_SECONDS = 8;
// Shortest clip (after trims) the fades and the caption have room for.
export const MIN_CLIP_SECONDS = 1;

export const brand = {
	name: 'Racks',
	tagline: 'A floating desktop organizer for Windows.',
	placeholderLabel: 'Placeholder footage',
};

export type ClipId = 'hero' | 'drag-in' | 'physics' | 'styling' | 'finder';
export type PlaceholderId = 'demo-1' | 'demo-2' | 'demo-3';

/**
 * One zoom/pan keyframe. `scale` 1 shows the whole clip; `x`/`y` (0 to 1) is the point of the
 * clip that stays fixed while zooming, so 0.5/0.5 zooms into the centre and 0/1 into the
 * bottom-left corner. Values ease (in-out cubic) from one keyframe to the next and hold
 * before the first and after the last.
 *
 * Keep it to one push-in, hold and release per clip: scale 1.15 to 1.25, ramps of at least 36
 * frames and a hold of at least 45 (1.5 s). Leave `zoom: []` when the action already fills the frame.
 */
export type ZoomKeyframe = {frame: number; scale: number; x: number; y: number};

/** A rectangle in the recording's own pixels (not the frame's). */
export type Rect = {x: number; y: number; w: number; h: number};

export type ClipConfig = {
	caption: string | null;
	/** Old demo GIF shown while docs/media/raw/<id>.mp4 does not exist yet. */
	placeholder: PlaceholderId;
	/** Dead time to cut from the start and end of the raw recording. */
	trimStartSeconds: number;
	trimEndSeconds: number;
	zoom: ZoomKeyframe[];
	/** Areas of the recording to blur, e.g. a user name in a file path. They follow the zoom. */
	redact?: Rect[];
};

export const clips: Record<ClipId, ClipConfig> = {
	hero: {
		caption: null,
		placeholder: 'demo-3',
		trimStartSeconds: 1.5,
		trimEndSeconds: 0.5,
		zoom: [],
	},
	'drag-in': {
		caption: 'Drag in. Done.',
		placeholder: 'demo-1',
		trimStartSeconds: 2.2,
		trimEndSeconds: 1.6,
		zoom: [],
	},
	physics: {
		caption: 'Physics that feel real',
		placeholder: 'demo-2',
		trimStartSeconds: 2.2,
		trimEndSeconds: 3.0,
		zoom: [],
	},
	styling: {
		caption: 'A rack for every mood',
		placeholder: 'demo-1',
		trimStartSeconds: 3.4,
		trimEndSeconds: 4.8,
		// One push-in on the settings panel and the rack once the panel is open.
		zoom: [
			{frame: 75, scale: 1, x: 0.5, y: 0.5},
			{frame: 115, scale: 1.35, x: 0, y: 0.62},
			{frame: 430, scale: 1.35, x: 0, y: 0.62},
			{frame: 465, scale: 1, x: 0.5, y: 0.5},
		],
	},
	finder: {
		caption: 'Find anything instantly',
		placeholder: 'demo-2',
		trimStartSeconds: 2.2,
		trimEndSeconds: 2.6,
		zoom: [
			{frame: 24, scale: 1, x: 0.5, y: 0.5},
			{frame: 70, scale: 1.1, x: 0.5, y: 0.5},
			{frame: 270, scale: 1.1, x: 0.5, y: 0.5},
			{frame: 305, scale: 1, x: 0.5, y: 0.5},
		],
		// The path line of each result row (C:/Users/<name>/RacksWorkspace/...), which shows the Windows user name.
		redact: [434, 498, 562, 628].map((y) => ({x: 756, y, w: 262, h: 19})),
	},
};

/**
 * Framed loops rendered to out/<clip>.gif: the header-less hero for the top of the README and one
 * per feature section. (The Hero composition, with the brand intro, renders to out/hero.mp4.)
 */
export const loops: {compositionId: string; clip: ClipId}[] = [
	{compositionId: 'HeroLoop', clip: 'hero'},
	{compositionId: 'DragIn', clip: 'drag-in'},
	{compositionId: 'Physics', clip: 'physics'},
	{compositionId: 'Styling', clip: 'styling'},
	{compositionId: 'Finder', clip: 'finder'},
];

export const timing = {
	/** Hero: the brand lockup alone on screen (long enough to read the tagline) before it docks. */
	introFrames: 72,
	/** Hero: the tagline fades out over these last intro frames, before the lockup moves. */
	taglineFadeFrames: 9,
	/** Hero: how long the lockup takes to dock while the clip rises in. */
	dockFrames: 24,
	/** Loops: fade from and back to the bare background so the GIF loops cleanly. */
	loopFadeFrames: 10,
	/** Loops: when the caption starts sliding in. */
	captionDelayFrames: 12,
	/** Hero: fade back to the bare background (its first frame) so the video loops seamlessly. */
	heroOutFrames: 18,
};
