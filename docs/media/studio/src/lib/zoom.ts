import {Easing, interpolate} from 'remotion';
import type {ZoomKeyframe} from '../config';

const ease = Easing.inOut(Easing.cubic);

/** Camera state at `frame`: eased between the surrounding keyframes, held outside them. */
export const zoomAt = (frame: number, keyframes: ZoomKeyframe[]): Omit<ZoomKeyframe, 'frame'> => {
	if (keyframes.length === 0) {
		return {scale: 1, x: 0.5, y: 0.5};
	}
	const sorted = [...keyframes].sort((a, b) => a.frame - b.frame);
	const next = sorted.findIndex((k) => k.frame > frame);
	if (next === -1 || next === 0) {
		const {scale, x, y} = sorted[next === -1 ? sorted.length - 1 : 0];
		return {scale, x, y};
	}
	const from = sorted[next - 1];
	const to = sorted[next];
	const t = interpolate(frame, [from.frame, to.frame], [0, 1], {easing: ease});
	const mix = (a: number, b: number) => a + (b - a) * t;
	return {scale: mix(from.scale, to.scale), x: mix(from.x, to.x), y: mix(from.y, to.y)};
};
