import {Composition} from 'remotion';
import {Hero, calculateHeroMetadata} from './compositions/Hero';
import {Loop, calculateLoopMetadata} from './compositions/Loop';
import {ThemeReel, REEL_HEIGHT, REEL_WIDTH, THEME_REEL_FRAMES} from './compositions/ThemeReel';
import {FALLBACK_SECONDS, FPS, HEIGHT, loops, WIDTH} from './config';

// Durations here are only defaults; calculateMetadata replaces them with the probed clip lengths
// (and the fps with the `fps` prop, which the GIF render sets to 25).
const DEFAULT_FRAMES = FALLBACK_SECONDS * FPS;

export const RemotionRoot: React.FC = () => (
	<>
		<Composition
			id="Hero"
			component={Hero}
			width={WIDTH}
			height={HEIGHT}
			fps={FPS}
			durationInFrames={DEFAULT_FRAMES}
			defaultProps={{}}
			calculateMetadata={calculateHeroMetadata}
		/>
		<Composition
			id="ThemeReel"
			component={ThemeReel}
			width={REEL_WIDTH}
			height={REEL_HEIGHT}
			fps={FPS}
			durationInFrames={THEME_REEL_FRAMES}
		/>
		{loops.map(({compositionId, clip}) => (
			<Composition
				key={compositionId}
				id={compositionId}
				component={Loop}
				width={WIDTH}
				height={HEIGHT}
				fps={FPS}
				durationInFrames={DEFAULT_FRAMES}
				defaultProps={{clip}}
				calculateMetadata={calculateLoopMetadata}
			/>
		))}
	</>
);
