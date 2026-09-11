Use the validator launcher resolved from the loaded guidance. Work beneath ./out and set
READINESS_TEMP=./out/.validator-cache. The supplied package is already decoded by setup.

1. Run inputs discover, inputs confirm, and inputs validate with root ./fixture, package
   ./fixture/package.nupkg, candidates ./fixture/candidates.json, and outputs under ./out.
2. Initialize a unified assessment for component grid.
3. Run fixture-tool.py evidence-inputs on the initialized assessment. Use two records: the first
   says the exact artifact does not establish signing or an SBOM; the second says the supplied
   internal accessibility audit establishes its bounded claim.
4. Build and validate a repository ledger, bundle both evidence IDs, and run fixture-tool.py
   complete with profile mixed.
5. Run assessment validate, report render to ./out/revisions, and report verify on 0001.
Do not create decision-guidance.md.
