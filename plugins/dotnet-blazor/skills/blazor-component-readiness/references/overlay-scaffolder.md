# Scaffolder overlay

Assess the `scaffolder` family when the confirmed deliverable includes a supported scaffolder.
A sample generator or handwritten template is not enough without an explicit product claim.

Use the six `SCF-*` requirements exactly as defined in `rubric.json`. Collect evidence that the
scaffolder:

- integrates through `dotnet scaffold` and is contributed to `dotnet/scaffolding`, not `dotnet new`;
- produces output satisfying applicable core accessibility and Blazor requirements;
- uses only supported/approved dependencies;
- pins and verifies generated dependency versions and package identity;
- performs no arbitrary script execution;
- remains aligned with supported library versions.

Compile and exercise generated output rather than scoring only template source. For a package
with no in-scope scaffolder, retain all six canonical rows as N/A with explicit scope rationale.
An integration plan or contribution-ready artifact does not establish contribution. Close the
integration row with the actual `dotnet/scaffolding` contribution and the working `dotnet scaffold`
integration, not a promise to contribute it later.
