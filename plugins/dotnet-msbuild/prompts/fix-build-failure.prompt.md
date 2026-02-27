---
description: "Diagnose and fix a .NET/MSBuild build failure"
---

# Fix Build Failure

I have a build failure in my .NET project. Help me diagnose and fix it.

## Steps

1. Run the build with a binary log: `dotnet build /bl:debug.binlog`
2. If the build fails, analyze the binlog for errors (load it, get diagnostics)
3. Identify the root cause by checking:
   - Error codes (CS, MSB, NU, NETSDK prefixes)
   - Which project(s) failed
   - Whether it's a compile error, restore error, or SDK issue
4. Suggest specific fixes based on the error type
5. Apply the fix and rebuild to verify
6. If the fix doesn't work, iterate with more detailed analysis

## Context

- Check for `global.json` SDK constraints
- Check NuGet source configuration in `nuget.config`
- Check for `Directory.Build.props` / `Directory.Build.targets` that might affect the build
- Look at recent changes that might have caused the failure
