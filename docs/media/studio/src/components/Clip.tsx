import type {ZoomKeyframe} from '../config';
import type {Footage} from '../footage';
import {fitInside} from '../lib/fit';
import {frame as frameStyle} from '../theme';
import {Caption} from './Caption';
import {ClipMedia} from './ClipMedia';
import {PlaceholderTag} from './PlaceholderTag';

export type ClipProps = {
	footage: Footage;
	/** Largest area the frame may take; the frame keeps the footage's aspect ratio inside it. */
	box: {width: number; height: number};
	zoom: ZoomKeyframe[];
	caption: {text: string; delay: number} | null;
};

/** A recording in a rounded, softly shadowed frame, with an optional lower-third caption. */
export const Clip: React.FC<ClipProps> = ({footage, box, zoom, caption}) => {
	const {width, height} = fitInside(footage, box);

	return (
		<div
			style={{
				position: 'relative',
				width,
				height,
				borderRadius: frameStyle.radius,
				overflow: 'hidden',
				boxShadow: frameStyle.shadow,
				// Keeps the rounded clip crisp while the media inside is scaled.
				isolation: 'isolate',
			}}
		>
			<ClipMedia footage={footage} zoom={zoom} width={width} height={height} />
			{/* Hairline drawn on top so it stays visible over light footage. */}
			<div
				style={{
					position: 'absolute',
					inset: 0,
					borderRadius: frameStyle.radius,
					boxShadow: `inset ${frameStyle.border}`,
					pointerEvents: 'none',
				}}
			/>
			{footage.kind === 'placeholder' ? <PlaceholderTag /> : null}
			{caption ? <Caption text={caption.text} delay={caption.delay} /> : null}
		</div>
	);
};
