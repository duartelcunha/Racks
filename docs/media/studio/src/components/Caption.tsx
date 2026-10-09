import {spring} from 'remotion';
import {FPS} from '../config';
import {useConfigFrame} from '../lib/time';
import {calmSpring, colors, fontFamily} from '../theme';

/** Lower-third label that slides up into the bottom-left corner of the clip frame. */
export const Caption: React.FC<{text: string; delay: number}> = ({text, delay}) => {
	const enter = spring({frame: useConfigFrame() - delay, fps: FPS, config: calmSpring, durationInFrames: 22});

	return (
		<div
			style={{
				position: 'absolute',
				left: 36,
				bottom: 36,
				display: 'flex',
				alignItems: 'center',
				gap: 20,
				padding: '18px 32px 20px 26px',
				borderRadius: 20,
				// Nearly opaque, so the pill reads the same white over dark and light footage.
				background: 'rgba(255, 255, 255, 0.94)',
				backdropFilter: 'blur(24px)',
				boxShadow: '0 0 0 1px rgba(15, 27, 29, 0.06), 0 12px 32px -8px rgba(12, 48, 44, 0.28)',
				fontFamily,
				opacity: enter,
				transform: `translateY(${(1 - enter) * 22}px)`,
			}}
		>
			{/* Wide enough (4 px in the 960 px GIFs) to stay teal through GIF quantisation. */}
			<div style={{width: 8, height: 40, borderRadius: 4, background: colors.teal}} />
			<div style={{fontSize: 48, fontWeight: 600, letterSpacing: '-0.015em', color: colors.ink, lineHeight: 1.1}}>
				{text}
			</div>
		</div>
	);
};
