# Wide graph performance report

Build command captured:

```text
dotnet build Aggregator\Aggregator.csproj
```

## Project graph

Core -> App1
Core -> App2
Core -> App3
Core -> App4
App1, App2, App3, App4 -> Aggregator

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| ResolveProjectReferences | 31.5s | 68.0% |
| CoreCompile | 9.4s | 20.3% |
| CopyFilesToOutputDirectory | 2.1s | 4.5% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| Csc | 9.4s | 20.3% |
| Copy | 1.8s | 3.9% |
| ResolveAssemblyReference | 1.4s | 3.0% |

## Node timeline excerpt

| Project | Start | End |
| --- | ---: | ---: |
| Core | 0.0s | 4.2s |
| App1 | 4.2s | 9.8s |
| App2 | 9.8s | 15.4s |
| App3 | 15.4s | 21.0s |
| App4 | 21.0s | 26.6s |
| Aggregator | 26.6s | 30.8s |

Notes:
- The capture came from a plain dotnet build invocation with no explicit -m flag.
- The four app projects do not reference each other.
