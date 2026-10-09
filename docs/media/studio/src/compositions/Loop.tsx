import {AbsoluteFill, Easing, interpolate, useVideoConfig, type CalculateMetadataFunction} from 'remotion';
import {Background} from '../components/Background';
import {Clip} from '../components/Clip';
import {clips, FPS, HEIGHT, timing, WIDTH, type ClipId} from '../config';
import {resolveFootage} from '../footage';
import {useConfigFrame, useConfigLastFrame, type RenderProps} from '../lib/time';

export type LoopProps = RenderProps & {clip: ClipId};

// 64 px of background around the frame: enough for its shadow, as much footage as possible.
const MARGIN = 64;
const BOX = {width: WIDTH - 2 * MARGIN, height: HEIGHT - 2 * MARGIN};

export const calculateLoopMetadata: CalculateMetadataFunction<LoopProps> = ({props}) => {
	const fps = props.fps ?? FPS;
	return {fps, durationInFrames: resolveFootage(props.clip, fps).durationInFrames};
};

/** A framed clip that fades in from and out to the bare background, so the GIF loops cleanly. */
export const Loop: React.FC<LoopProps> = ({clip}) => {
	const t = useConfigFrame();
	const last = useConfigLastFrame();
	const {fps} = useVideoConfig();
	const {caption, zoom, redact} = clips[clip];
	const fade = timing.loopFadeFrames;

	const opacity = interpolate(t, [0, fade, last - fade, last], [0, 1, 1, 0], {
		extrapolateLeft: 'clamp',
		extrapolateRight: 'clamp',
	});
	const scale = interpolate(t, [0, fade], [0.985, 1], {
		extrapolateRight: 'clamp',
		easing: Easing.out(Easing.cubic),
	});

	return (
		<AbsoluteFill>
			<Background />
			<AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
				<div style={{opacity, transform: `scale(${scale})`}}>
					<Clip
						footage={resolveFootage(clip, fps)}
						box={BOX}
						zoom={zoom}
						redact={redact}
						caption={caption ? {text: caption, delay: timing.captionDelayFrames} : null}
					/>
				</div>
			</AbsoluteFill>
		</AbsoluteFill>
	);
};
