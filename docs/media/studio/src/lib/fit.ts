/** Largest size with the media's aspect ratio that fits inside the box, rounded to whole pixels. */
export const fitInside = (
	media: {width: number; height: number},
	box: {width: number; height: number},
): {width: number; height: number} => {
	const scale = Math.min(box.width / media.width, box.height / media.height);
	return {width: Math.round(media.width * scale), height: Math.round(media.height * scale)};
};
