#!/usr/bin/env node
import './sdk-startup.mjs';

// Load the CLI only after the shared SDK class has its startup guard.
await import('./node_modules/@microsoft/vally-cli/dist/index.js');
