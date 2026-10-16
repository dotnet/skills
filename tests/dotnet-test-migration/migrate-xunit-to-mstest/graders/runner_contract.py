"""Verify the evaluated target framework and VSTest project mode."""

import json
import subprocess


properties = (
    "TargetFramework,EnableMSTestRunner,UseMicrosoftTestingPlatformRunner,"
    "TestingPlatformDotnetTestSupport,IsTestingPlatformApplication"
)
result = subprocess.run(
    ["dotnet", "msbuild", "TestProject.csproj", "-getProperty:" + properties],
    capture_output=True, text=True, timeout=60,
)
if result.returncode != 0:
    raise RuntimeError(result.stdout + result.stderr)
values = json.loads(result.stdout)["Properties"]
assert values["TargetFramework"] == "net8.0", values
for name in properties.split(",")[1:]:
    assert values[name].lower() != "true", (name, values[name])
print("RUNNER_CONTRACT:net8.0 VSTest, no native or bridged MTP")
