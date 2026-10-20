# Scoped toolchain troubleshooting

Start with the first actionable error and the build's selected paths/versions.
Do not reinstall the entire environment in response to a generic failure.

| Evidence | Diagnosis / next check | Fix only when authorized |
|---|---|---|
| Pinned SDK cannot resolve | Read applicable `global.json`, installed SDKs and roll-forward policy | Install the missing intended SDK or deliberately revise policy |
| Workload missing after SDK switch | Compare SDK feature band and effective installed workloads | Restore/install required project workloads under the intended pin |
| Installed pack missing/corrupt | Establish corruption, not just a version mismatch | `workload repair` reinstalls installed packs |
| Unsupported Java version | Inspect build-selected `JavaSdkDirectory`, java and javac | Select/install a JDK satisfying that workload's requirements |
| Different JDK vendor | Vendor alone is not a root cause | Recommend tested Microsoft OpenJDK if replacement is actually needed |
| JAVA_HOME unset | Check whether the build resolved a valid JDK | No change if selection works; explicit paths may help if it doesn't |
| Android SDK not found | Check actual `AndroidSdkDirectory`, env/IDE overrides, directory existence | Correct the scoped path before downloading another SDK |
| Android platform/build-tools missing | Compare installed packages with manifest and project target API | Project-aware `InstallAndroidDependencies` or exact missing sdkmanager packages |
| License failure | Report specific license and required user acceptance | Accept only with approval |
| Xcode mismatch | Compare selected Xcode with the effective Apple workload | Select a compatible installed Xcode or plan a coordinated upgrade |

Use `microsoft-openjdk.md` for Java selection, `workload-dependencies-discovery.md`
for exact metadata, and the matching platform reference only when needed.

Restore/build logs can contain private paths and credentials. Keep investigation
local and report only relevant errors; don't publish full logs automatically.
Distinguish static inspection from an actual successful target build. Signing
and runtime app failures should be redirected to the relevant workflow.
