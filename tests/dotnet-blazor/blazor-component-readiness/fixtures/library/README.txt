Use the validator launcher resolved from the loaded guidance. Work beneath ./out and set
READINESS_TEMP=./out/.validator-cache.

Copy the decoded package and the package-level and two component documentation files into ./out.
Create and validate confirmed package, grid, and chart input manifests beneath ./out from the three
supplied candidate files, using ./out as the input root. Create a complete 46-row package assessment and revision. Create separate complete
64-row grid and chart assessments, each initialized with and verified against that exact package
revision. For every unit, use fixture-tool.py to prepare one bounded evidence record, build and
validate the correct repository or component ledger, bundle it, complete the initialized
assessment, validate, render, and report verify.

Use fixture-tool.py inventory-candidates with --root ./out and those exact confirmed input paths, then run inventory
discover and confirm. Render revisions at the canonical revision roots named by the resulting
inventory. Use the immutable revisions to run library reconcile, library validate, and library
index, producing ./out/library-index.json and ./out/library-index.md.
Do not create decision-guidance.md and do not touch any remote.
