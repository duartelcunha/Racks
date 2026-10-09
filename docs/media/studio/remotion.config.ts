// Applies to the Remotion CLI and Studio (`npx remotion studio`, `npx remotion still`).
// The npm render scripts call the Node APIs directly and set the same options there.
import {Config} from '@remotion/cli/config';

Config.setEntryPoint('src/index.ts');
Config.setVideoImageFormat('jpeg');
Config.setJpegQuality(95);
Config.setOverwriteOutput(true);
