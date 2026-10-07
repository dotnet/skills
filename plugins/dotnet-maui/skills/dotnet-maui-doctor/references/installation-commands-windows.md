# Windows installation scope

For Windows targets, inspect the project's Windows TFM/platform version and
installed Windows SDKs. Visual Studio Installer or the official Windows SDK
installer can supply missing components after approval. Android-only requests
do not require Windows SDK remediation.

Use `installation-commands.md` for workload-set and Android dependency plans.
PowerShell invokes tools via `&`; Android's manual tool is `sdkmanager.bat` at
the actual SDK command-line-tools path. Quote absolute paths and semicolon
package IDs. Do not change PATH, JAVA_HOME, registry or hypervisor settings
merely to make a health check pass.
