# Contoso general build performance summary

## Target Performance Summary

| Target | Time | Share |
| --- | ---: | ---: |
| Restore | 16.0s | 18.6% |
| CoreCompile | 15.2s | 17.7% |
| CopyFilesToOutputDirectory | 14.1s | 16.4% |
| ResolveAssemblyReferences | 10.6s | 12.3% |
| ResolveProjectReferences | 9.8s | 11.4% |

## Task Performance Summary

| Task | Self time | Share |
| --- | ---: | ---: |
| RestoreTask | 16.0s | 18.6% |
| Csc | 14.8s | 17.2% |
| Copy | 13.9s | 16.2% |
| ResolveAssemblyReference | 10.4s | 12.1% |
| GenerateResource | 5.0s | 5.8% |

Notes:
- The request is for a general triage across the whole report.
- No single target exceeds 20% of the total build time.
