import {useCurrentFrame, useVideoConfig} from 'remotion';
import {FPS} from '../config';

/** Props every composition takes: the frame rate to render at (FPS when omitted; the GIFs pass 25). */
export type RenderProps = {fps?: number};

/** Config frames (counted at FPS) to frames at `fps`. */
export const toFrames = (configFrames: number, fps: number): number => Math.round((configFrames * fps) / FPS);

/**
 * The current time in config frames (counted at FPS), whatever rate the composition renders at,
 * so the timings and zoom keyframes in config.ts mean the same thing in the MP4 and the GIFs.
 * Pair it with `fps: FPS` in spring().
 */
export const useConfigFrame = (): number => {
	const frame = useCurrentFrame();
	const {fps} = useVideoConfig();
	return (frame * FPS) / fps;
};

/** The last frame of the composition, in config frames. */
export const useConfigLastFrame = (): number => {
	const {fps, durationInFrames} = useVideoConfig();
	return ((durationInFrames - 1) * FPS) / fps;
};
