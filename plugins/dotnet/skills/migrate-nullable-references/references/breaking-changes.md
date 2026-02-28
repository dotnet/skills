# Breaking Changes from NRT Annotations (Libraries)

For libraries consumed by other projects, NRT annotations are part of the public API contract. Incorrect annotations are source-breaking changes for consumers:

- **Making a parameter non-nullable when it should be nullable**: If consumers previously passed null to a parameter and the method handled it gracefully, marking that parameter as `T` (non-nullable) causes compile warnings or errors for those callers. For example, annotating a logging enricher's `value` parameter as `object` instead of `object?` when the method has always accepted null values would break every caller that passes null.
- **Implicit non-nullability of unannotated types**: Enabling `<Nullable>enable</Nullable>` implicitly makes every unannotated reference-type parameter non-nullable. If the method previously accepted null without throwing, this is a silent contract change. Scan all public methods for parameters that tolerate null.
- **Return types that can be null**: If a method can return null, the return type must be `T?`. Marking it as `T` hides a potential `NullReferenceException` from callers who trust the annotation.
- **Ship annotations in a minor version, not a patch**: Because annotations can cause new warnings for consumers (especially those using `TreatWarningsAsErrors`), treat the NRT migration as a minor version bump, not a patch. Document the change in release notes.
