// Visual tokens. Copy and timing live in config.ts.
export const colors = {
	teal: '#1AAE9F',
	ink: '#0F1B1D',
	inkMuted: '#52615F',
	backgroundTop: '#F5FAF9',
	backgroundBottom: '#E1EEEC',
};

export const fontFamily =
	'"Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';

export const frame = {
	radius: 22,
	border: '0 0 0 1px rgba(15, 27, 29, 0.08)',
	// Reaches about 54 px below the frame; the layouts leave at least 64 px of background there.
	shadow: '0 2px 4px rgba(15, 27, 29, 0.05), 0 18px 48px -12px rgba(12, 64, 58, 0.30)',
};

// No overshoot: everything settles, nothing bounces.
export const calmSpring = {damping: 200};
// A touch of life for the brand icon only.
export const iconSpring = {damping: 14, mass: 0.7};
