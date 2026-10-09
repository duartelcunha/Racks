import {brand} from '../config';
import {fontFamily} from '../theme';

/** Small corner tag shown while old demo GIFs stand in for the real recordings. */
export const PlaceholderTag: React.FC = () => (
	<div
		style={{
			position: 'absolute',
			top: 24,
			right: 24,
			padding: '8px 16px 9px',
			borderRadius: 999,
			background: 'rgba(15, 27, 29, 0.62)',
			color: 'rgba(255, 255, 255, 0.92)',
			fontFamily,
			// 13 px in the 960 px GIFs: still legible after downscaling and dithering.
			fontSize: 26,
			fontWeight: 500,
			letterSpacing: '0.01em',
		}}
	>
		{brand.placeholderLabel}
	</div>
);
