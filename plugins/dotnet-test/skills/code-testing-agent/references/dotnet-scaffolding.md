# .NET zero-to-one scaffolding decisions

Use this only when the requested production project has no suitable test project.

## Decisions that change the result

| Situation | Do | Never |
|-----------|----|-------|
| A compatible test project already references the target | Reuse it | Create a duplicate because its name/layout differs from your preference |
| Repository uses central package management | Add versionless package references; keep versions in `Directory.Packages.props` | Put `Version=` in the new test project |
| CI entry point is `.sln` or `.slnx` | Add the test project to that exact artifact | Validate only the test `.csproj` |
| CI entry point is `.slnf` | Add the project to the underlying solution **and** the filter's `solution.projects` list | Update only the underlying solution; the filter will still hide the tests |
| Repository has no solution by design | Keep project-oriented commands | Create a solution for aesthetics |
| Several production projects are present but one behavior is requested | Create/reuse one bounded test project for that behavior's owner | Scaffold a test project per assembly |

## Required sequence

1. Discover the exact build/test entry point and neighboring test conventions.
2. Use `dotnet new` for the repository's framework, then align its target framework,
   runner, nullable settings, and package-management style.
3. Add only production `ProjectReference` entries required by the planned tests.
4. Delete template tests and add a deterministic test of a real production symbol.
5. Run the test project directly.
6. Run the repository's real solution/filter/root command and confirm it discovers
   the new test. A direct green test project with zero harness discovery is failure.

For `.slnf`, preserve the file's JSON path style and add the test project relative
to the underlying solution. Verify both:

```text
dotnet sln <solution> list
dotnet test <filter.slnf>
```
