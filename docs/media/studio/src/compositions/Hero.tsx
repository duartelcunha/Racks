import {
	AbsoluteFill,
	interpolate,
	Sequence,
	spring,
	useVideoConfig,
	type CalculateMetadataFunction,
} from 'remotion';
import {Background} from '../components/Background';
import {Clip} from '../components/Clip';
import {Intro} from '../components/Intro';
import {clips, FPS, HEIGHT, timing, WIDTH} from '../config';
import {resolveFootage} from '../footage';
import {toFrames, useConfigFrame, useConfigLastFrame, type RenderProps} from '../lib/time';
import {calmSpring} from '../theme';

// Docked header: the lockup shrinks to this scale and moves up by this many pixels, which puts
// the icon and wordmark in the middle of the band above CLIP_TOP.
const DOCKED_SCALE = 0.36;
const DOCKED_SHIFT = -464;
// The clip fills the space below the docked header.
const CLIP_TOP = 120;
const CLIP_BOTTOM_MARGIN = 64;
const BOX = {width: WIDTH - 2 * CLIP_BOTTOM_MARGIN, height: HEIGHT - CLIP_TOP - CLIP_BOTTOM_MARGIN};
// The clip starts (and rises in) a beat after the lockup starts docking, so the two never cross mid-flight.
const CLIP_START = timing.introFrames + 10;

export const calculateHeroMetadata: CalculateMetadataFunction<RenderProps> = ({props}) => {
	const fps = props.fps ?? FPS;
	return {fps, durationInFrames: toFrames(CLIP_START, fps) + resolveFootage('hero', fps).durationInFrames};
};

/**
 * Branded hero video: the lockup springs in, docks to the top as the hero clip rises into place,
 * and the end fades back to the bare background (the first frame) so the video loops seamlessly.
 * The README GIF uses the header-less HeroLoop instead, since the README already shows the lockup.
 */
export const Hero: React.FC<RenderProps> = () => {
	const t = useConfigFrame();
	const last = useConfigLastFrame();
	const {fps} = useVideoConfig();
	const {zoom, caption} = clips.hero;

	const dock = spring({frame: t - timing.introFrames, fps: FPS, config: calmSpring, durationInFrames: timing.dockFrames});
	const rise = spring({frame: t - CLIP_START, fps: FPS, config: calmSpring, durationInFrames: timing.dockFrames});
	const clamp = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'} as const;
	// The tagline is gone before the lockup starts to move, so it never ghosts over the rising clip.
	const taglineOpacity = interpolate(t, [timing.introFrames - timing.taglineFadeFrames, timing.introFrames], [1, 0], clamp);
	const loopOut = interpolate(t, [last - timing.heroOutFrames, last], [1, 0], clamp);

	return (
		<AbsoluteFill>
			<Background />
			<AbsoluteFill style={{opacity: loopOut}}>
				<AbsoluteFill
					style={{
						alignItems: 'center',
						justifyContent: 'center',
						transform: `translateY(${dock * DOCKED_SHIFT}px) scale(${1 - dock * (1 - DOCKED_SCALE)})`,
					}}
				>
					<Intro taglineOpacity={taglineOpacity} />
				</AbsoluteFill>
				<Sequence from={toFrames(CLIP_START, fps)}>
					<AbsoluteFill
						style={{
							alignItems: 'center',
							paddingTop: CLIP_TOP,
							opacity: rise,
							transform: `translateY(${(1 - rise) * 70}px)`,
						}}
					>
						<Clip
							footage={resolveFootage('hero', fps)}
							box={BOX}
							zoom={zoom}
							caption={caption ? {text: caption, delay: timing.captionDelayFrames + timing.dockFrames} : null}
						/>
					</AbsoluteFill>
				</Sequence>
			</AbsoluteFill>
		</AbsoluteFill>
	);
};
