#!/usr/bin/env node
import './sdk-startup.mjs';

if (process.env.VALLY_JUDGE_USAGE_LOG) await import('./judge-usage-tap.mjs');

// Load the CLI only after the shared SDK class has its startup guard.
await import('./node_modules/@microsoft/vally-cli/dist/index.js');
