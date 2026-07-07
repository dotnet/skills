# InternalsVisibleTo — `internal` is not private across the solution

## Contents

- The hazard
- Find the friend assemblies
- Strong-named friends
- What to do

## The hazard

`internal` limits access to the declaring assembly **unless** the assembly grants friend access via
`[assembly: InternalsVisibleTo("Other.Assembly")]`. Test projects and split implementation assemblies use
this constantly. So an internal rename, move, signature change, or removal can break a consumer that has
**no project reference visible from the declaring project** — the coupling is expressed in an attribute,
not a reference graph.

## Find the friend assemblies

```bash
grep -rn "InternalsVisibleTo" --include=*.cs --include=*.csproj --include=*.props .
```

`InternalsVisibleTo` can live in a `.cs` (`AssemblyInfo`/any file) **or** as an MSBuild
`<InternalsVisibleTo>` item in a `.csproj`/`Directory.Build.props`. Enumerate every named friend, then
search **those** assemblies for uses of the internal symbol you are changing — not just the declaring
project.

## Strong-named friends

When the declaring assembly is strong-named, the `InternalsVisibleTo` string includes the friend's full
`PublicKey=...`. Renaming or re-signing a friend assembly, or changing keys, breaks the grant. Do not alter
the assembly name/key half of the relationship as an incidental part of another change.

## What to do

- Treat internal members that friends consume with the **same care as public API**: search all friend
  assemblies for binding references before renaming/moving/removing.
- If the change is large, let the **compiler across the whole solution** (build the friend projects too) be
  the safety net — a missed reference becomes a build error in the friend project, not a silent break.
- Adding a new friend (`InternalsVisibleTo`) to make a change "reachable" is itself a surface change — flag
  it rather than doing it silently.
