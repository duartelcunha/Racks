import {Img, spring, staticFile} from 'remotion';
import {brand, FPS} from '../config';
import {useConfigFrame} from '../lib/time';
import {calmSpring, colors, fontFamily, iconSpring} from '../theme';

const ICON_SIZE = 128;

/**
 * Brand lockup (icon, wordmark, tagline) with its entrance. Starts fully transparent on frame 0.
 * `taglineOpacity` lets the Hero fade the tagline out before the lockup docks as a header.
 */
export const Intro: React.FC<{taglineOpacity: number}> = ({taglineOpacity}) => {
	const frame = useConfigFrame();
	const icon = spring({frame, fps: FPS, config: iconSpring, durationInFrames: 30});
	const word = spring({frame: frame - 5, fps: FPS, config: calmSpring, durationInFrames: 24});
	const tagline = spring({frame: frame - 13, fps: FPS, config: calmSpring, durationInFrames: 24});

	return (
		<div style={{display: 'flex', flexDirection: 'column', alignItems: 'center', fontFamily}}>
			<div style={{display: 'flex', alignItems: 'center', gap: 36}}>
				<Img
					src={staticFile('repo/icon.png')}
					style={{
						width: ICON_SIZE,
						height: ICON_SIZE,
						opacity: Math.min(1, icon * 1.5),
						transform: `scale(${0.82 + 0.18 * icon})`,
					}}
				/>
				<div
					style={{
						fontSize: 140,
						fontWeight: 600,
						letterSpacing: '-0.035em',
						lineHeight: 1,
						color: colors.ink,
						opacity: word,
						transform: `translateX(${(1 - word) * -18}px)`,
						// Optical alignment: the cap height sits a little high in the line box.
						paddingBottom: 10,
					}}
				>
					{brand.name}
				</div>
			</div>
			<div
				style={{
					marginTop: 30,
					fontSize: 42,
					fontWeight: 400,
					letterSpacing: '-0.005em',
					color: colors.inkMuted,
					opacity: tagline * taglineOpacity,
					transform: `translateY(${(1 - tagline) * 14}px)`,
				}}
			>
				{brand.tagline}
			</div>
		</div>
	);
};
