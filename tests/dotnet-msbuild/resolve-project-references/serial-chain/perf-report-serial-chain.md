# Serial chain performance report

Build command captured:

```text
dotnet build Tests\Tests.csproj -m -bl
```

## Project graph

Core -> Api -> Web -> Tests

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| ResolveProjectReferences | 48.6s | 72.0% |
| CoreCompile | 11.7s | 17.3% |
| CopyFilesToOutputDirectory | 3.9s | 5.8% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| Csc | 37.8s | 56.0% |
| Copy | 3.1s | 4.6% |
| ResolveAssemblyReference | 2.2s | 3.3% |

## Node timeline excerpt

| Project | Start | End |
| --- | ---: | ---: |
| Core | 0.0s | 8.7s |
| Api | 8.7s | 19.6s |
| Web | 19.6s | 32.0s |
| Tests | 32.0s | 45.1s |

Notes:
- Each project starts only after the previous project finishes.
- There are no independent sibling projects in this fixture.
