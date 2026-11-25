# macOS scoped diagnostics

For Xcode selection errors inspect `xcode-select -p`, `xcodebuild -version`
and any build-scoped `DEVELOPER_DIR`/IDE override. `/Library/Developer/CommandLineTools`
is not a full Xcode installation. Compare the selected Xcode with the effective
Apple manifest before suggesting installation or a selection change.

For Java/Android failures, inspect build-selected directories; use
`/usr/libexec/java_home -V` only if JDK inventory is needed. No Xcode inventory
is necessary for Android-only diagnosis.

Simulator problems require the actual device/runtime error and
`xcrun simctl list devices available` / `xcrun simctl list runtimes`.
Do not erase simulators or download runtimes during a build-only health check.
Provisioning/signing and runtime app logic are separate from this toolchain scope.
