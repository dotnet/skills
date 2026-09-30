# AssetsPipeline performance report

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| ResolveProjectReferences | 54.2s | 61.0% |
| CopyFilesToOutputDirectory | 21.0s | 23.6% |
| CoreCompile | 18.2s | 20.5% |
| ResolveAssemblyReferences | 6.1s | 6.9% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| Copy | 18.4s | 20.7% |
| Csc | 16.1s | 18.1% |
| ResolveAssemblyReference | 5.9s | 6.6% |
| MSBuild | 4.4s | 4.9% |

Notes:
- 3,812 files were copied to output folders.
- Hardlinks are disabled on the current agents.
