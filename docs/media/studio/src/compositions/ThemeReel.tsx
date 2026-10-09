import {AbsoluteFill, Img, interpolate, staticFile, useCurrentFrame} from 'remotion';
import {fontFamily} from '../theme';

// Real screenshots of one rack wearing each one-click theme, captured from the running app
// (public/themes/NN-<slug>.png, 1920x1080). They cross-fade into docs/media/raw/styling.mp4,
// which the Styling composition then frames like any other recording.
export const THEMES = [
	{file: '01-dark.png', label: 'Dark'},
	{file: '02-light.png', label: 'Light'},
	{file: '03-glass.png', label: 'Glass'},
	{file: '04-neon.png', label: 'Neon'},
	{file: '05-solarized-dark.png', label: 'Solarized Dark'},
	{file: '06-solarized-light.png', label: 'Solarized Light'},
] as const;

export const THEME_HOLD_FRAMES = 45;
export const THEME_FADE_FRAMES = 9;
export const THEME_REEL_FRAMES = THEMES.length * THEME_HOLD_FRAMES;
// The recordings drop the taskbar, so the stills are cropped to the same 1920x1032.
export const REEL_WIDTH = 1920;
export const REEL_HEIGHT = 1032;

const opacityAt = (frame: number, index: number) => {
	const start = index * THEME_HOLD_FRAMES;
	if (index === 0) return 1;
	return interpolate(frame, [start - THEME_FADE_FRAMES, start], [0, 1], {
		extrapolateLeft: 'clamp',
		extrapolateRight: 'clamp',
	});
};

export const ThemeReel: React.FC = () => {
	const frame = useCurrentFrame();
	const active = Math.min(THEMES.length - 1, Math.floor(frame / THEME_HOLD_FRAMES));
	const pillIn = interpolate(frame - active * THEME_HOLD_FRAMES, [0, THEME_FADE_FRAMES], [0, 1], {
		extrapolateRight: 'clamp',
	});

	return (
		<AbsoluteFill style={{backgroundColor: '#12283a', overflow: 'hidden'}}>
			{THEMES.map((theme, i) => (
				<Img
					key={theme.file}
					src={staticFile(`themes/${theme.file}`)}
					style={{position: 'absolute', left: 0, top: 0, width: 1920, height: 1080, opacity: opacityAt(frame, i)}}
				/>
			))}
			<div
				style={{
					position: 'absolute',
					left: 960,
					top: 150,
					transform: `translateX(-50%) translateY(${(1 - pillIn) * 8}px)`,
					opacity: pillIn,
					padding: '10px 26px',
					borderRadius: 999,
					background: 'rgba(15, 27, 29, 0.72)',
					color: '#fff',
					fontFamily,
					fontSize: 34,
					fontWeight: 600,
					letterSpacing: 0.2,
				}}
			>
				{THEMES[active].label}
			</div>
		</AbsoluteFill>
	);
};
