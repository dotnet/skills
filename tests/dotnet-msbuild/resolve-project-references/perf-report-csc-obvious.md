# CatalogService performance report

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| CoreCompile | 78.4s | 78.0% |
| CopyFilesToOutputDirectory | 6.3s | 6.3% |
| ResolveProjectReferences | 4.2s | 4.2% |
| ResolveAssemblyReferences | 3.7s | 3.7% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| Csc | 61.5s | 61.2% |
| GenerateResource | 8.9s | 8.9% |
| Copy | 6.1s | 6.1% |
| ResolveAssemblyReference | 3.5s | 3.5% |

Notes:
- Developers report that compilation and analyzers are what they notice in inner-loop builds.
- No one is asking to optimize project-reference waiting.
