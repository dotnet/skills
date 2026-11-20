# Linux Android scope

Linux is not a host for iOS, Mac Catalyst, or Windows MAUI builds. For Android
CI use `maui-android`, not the full `maui` umbrella. Check the selected SDK's
Linux distribution/architecture requirements and MAUI release support separately;
do not imply all MAUI tooling has the same Linux support as Windows/macOS.

Requirements are the project-resolved SDK/workload set, compatible JDK and
Android SDK packages. Microsoft OpenJDK is recommended/tested, not a rule that
all other vendors necessarily fail. Use the manifest's version range and
project API target to discover exact requirements.

Headless builds do not need an emulator, KVM or system images. Do not install
Apple/Windows workloads or modify hypervisor/system Java defaults.
