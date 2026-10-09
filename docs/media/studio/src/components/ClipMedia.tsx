import {Gif} from '@remotion/gif';
import {OffthreadVideo, staticFile} from 'remotion';
import type {ZoomKeyframe} from '../config';
import type {Footage} from '../footage';
import {useConfigFrame} from '../lib/time';
import {zoomAt} from '../lib/zoom';

/** The recording (or its placeholder GIF) filling the frame, with the keyframed zoom/pan applied. */
export const ClipMedia: React.FC<{footage: Footage; zoom: ZoomKeyframe[]; width: number; height: number}> = ({
	footage,
	zoom,
	width,
	height,
}) => {
	const {scale, x, y} = zoomAt(useConfigFrame(), zoom);
	const src = staticFile(footage.src);

	return (
		<div style={{width, height, transform: `scale(${scale})`, transformOrigin: `${x * 100}% ${y * 100}%`}}>
			{footage.kind === 'video' ? (
				<OffthreadVideo
					src={src}
					muted
					trimBefore={footage.trimBeforeFrames}
					style={{width, height, objectFit: 'cover'}}
				/>
			) : (
				<Gif src={src} width={width} height={height} fit="cover" />
			)}
		</div>
	);
};
