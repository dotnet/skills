## Developer Complaint

Our developers report that running `dotnet build` from the terminal feels sluggish
for incremental builds. Even when nothing has changed, CLI builds take 2-4 seconds,
while the same project rebuilds nearly instantly inside Visual Studio.

We've already verified:
- No analyzers are slowing things down
- Incremental build targets are working (targets do skip correctly)
- The project itself compiles in <500ms

The overhead seems to come from MSBuild process startup and re-evaluation of the
project graph on every CLI invocation.

We're looking for ways to speed up the developer inner loop when building from the
command line, especially for quick edit-build-test cycles.
