# Triage: Microsoft.EntityFrameworkCore.Design dependency conflict (dotnet/efcore#38718)

**Issue:** [Microsoft.EntityFrameworkCore.Design 的依赖问题，导致无法正常迁移数据库](https://github.com/dotnet/efcore/issues/38718)

## Summary

The user receives a NuGet version conflict error when trying to run EF Core migrations with `Microsoft.EntityFrameworkCore.Design 10.0.10`:

```
Microsoft.CodeAnalysis.Common 中检测到版本冲突。
  Cnfzh.Web.Entry -> Microsoft.EntityFrameworkCore.Design 10.0.10
      -> Microsoft.CodeAnalysis.CSharp 5.6.0
          -> Microsoft.CodeAnalysis.Common (= 5.6.0)
  Cnfzh.Web.Entry -> Microsoft.EntityFrameworkCore.Design 10.0.10
      -> Microsoft.CodeAnalysis.CSharp.Workspaces 5.0.0
          -> Microsoft.CodeAnalysis.Common (= 5.0.0)
```

## Root Cause

`Microsoft.EntityFrameworkCore.Design 10.0.10` declares minimum-version (`>=`) NuGet dependencies on both `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Workspaces`, both starting at `5.0.0`. In the published package, these are at the same version and are consistent.

However, if another package in the project (such as a .NET SDK component, a Roslyn analyzer, or a Visual Studio extension) introduces a transitive dependency on `Microsoft.CodeAnalysis.CSharp >= 5.6.0`, NuGet will upgrade `Microsoft.CodeAnalysis.CSharp` to `5.6.0` during resolution. Because nothing else requires a higher minimum for `Microsoft.CodeAnalysis.CSharp.Workspaces`, it stays at `5.0.0`.

The conflict arises because:
- `Microsoft.CodeAnalysis.CSharp 5.6.0` requires `Microsoft.CodeAnalysis.Common = 5.6.0` (exact pin)
- `Microsoft.CodeAnalysis.CSharp.Workspaces 5.0.0` requires `Microsoft.CodeAnalysis.Common = 5.0.0` (exact pin)

NuGet cannot satisfy both exact constraints simultaneously and reports a version conflict.

## Workaround

Add an explicit package reference to `Microsoft.CodeAnalysis.CSharp.Workspaces` in your project file, pinning it to the **same version** as the `Microsoft.CodeAnalysis.CSharp` that NuGet is resolving (in this case `5.6.0`). This forces NuGet to upgrade `CSharp.Workspaces` to `5.6.0` as well, making both packages require the same `Common` version.

In your `.csproj`:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.6.0" />
```

Or, if you are using `Directory.Packages.props` (central package management):

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.6.0" />
```

> **Tip:** If you are unsure which version of `Microsoft.CodeAnalysis.CSharp` is being resolved, run  
> `dotnet list package --include-transitive` and look for `Microsoft.CodeAnalysis.CSharp` in the output.  
> Use that same version for `Microsoft.CodeAnalysis.CSharp.Workspaces`.

## Notes

- Directly adding `Microsoft.CodeAnalysis.Common` (as Visual Studio may suggest) is **not** sufficient on its own. It resolves the `Common` conflict for the runtime project, but migrations still fail because `CSharp.Workspaces` and `CSharp` are at mismatched versions.
- EF Core's `Microsoft.EntityFrameworkCore.Design` packages `CSharp` and `CSharp.Workspaces` at the same version. The mismatch is caused by an external package in the user's solution pushing `CSharp` to a higher version without a corresponding upgrade to `CSharp.Workspaces`.
- This is not a defect in EF Core 10.0.10 itself; it is a NuGet resolution interaction with another Roslyn dependency in the user's project.

## Disposition

Closed as a configuration/usage question. The workaround above resolves the issue.
