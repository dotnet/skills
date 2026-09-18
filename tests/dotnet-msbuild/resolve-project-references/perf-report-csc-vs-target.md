# AppBuild performance report

Command captured:

```text
dotnet build AppBuild.sln -m -bl
```

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| ResolveProjectReferences | 63.7s | 67.4% |
| CoreCompile | 13.1s | 13.9% |
| CopyFilesToOutputDirectory | 7.0s | 7.4% |
| ResolveAssemblyReferences | 5.4s | 5.7% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| Csc | 42.1s | 44.6% |
| Copy | 6.5s | 6.9% |
| ResolveAssemblyReference | 5.8s | 6.1% |
| MSBuild | 4.9s | 5.2% |
| Message | 0.2s | 0.2% |

Notes:
- The team proposed removing project references or overriding ResolveProjectReferences.
- The build used four worker nodes and did not report a single failed project.
