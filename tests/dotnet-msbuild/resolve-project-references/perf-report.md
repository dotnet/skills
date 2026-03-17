## Build Performance Report

We profiled our project and found that `ResolveProjectReferences` dominates the
Target Performance Summary, consuming ~70% of the total build time for the App project.

The team is considering ways to optimize or eliminate this target. Some ideas
discussed include:
- Removing project references and using NuGet packages instead
- Trying to skip ResolveProjectReferences with a custom target override
- Consolidating all projects into one

We'd like an expert analysis of whether ResolveProjectReferences is actually the
bottleneck, and if so, what to do about it.
