import {AbsoluteFill} from 'remotion';
import {colors} from '../theme';

/** Soft, teal-tinted light backdrop. Static and low in gradient steps, so GIF frames compress well. */
export const Background: React.FC = () => (
	<AbsoluteFill
		style={{
			background: [
				'radial-gradient(60% 70% at 88% 6%, rgba(26, 174, 159, 0.16), rgba(26, 174, 159, 0) 70%)',
				`linear-gradient(160deg, ${colors.backgroundTop}, ${colors.backgroundBottom})`,
			].join(', '),
		}}
	/>
);
