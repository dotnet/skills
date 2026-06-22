# Common pitfalls

Real-world failure modes for `configure-agentic-perf-rules` — observed
during dogfooding (interview-coach v2 first install, behavioral coach
no-op re-run, ELI5Agent fresh install).

## Sentinel parsing (FAIL CLOSED, always)

- **Lenient sentinel matching.** The BEGIN and END regexes in step 2
  are intentionally strict (anchored, full-line, exact version
  pattern). Do not "fix up" a malformed sentinel by partial-matching
  or by accepting trailing whitespace beyond what `\s*$` allows.
  Auto-repair of corrupted sentinels is forbidden — the user might
  have intentionally renamed the block while migrating, and silent
  repair would clobber that.
- **Refusing to edit on multiple BEGIN/END.** If the file contains
  more than one BEGIN or END, abort with a chat message that names
  every offending line number. Multiple managed blocks usually mean
  a merge conflict went unresolved; appending a third would make it
  worse.
- **Out-of-order sentinels.** BEGIN must appear before its matching
  END. If you find END before BEGIN, treat as malformed and abort —
  do not swap them.

## Threshold preservation

- **Losing user-edited threshold values on update.** Step 2's
  "Threshold preservation algorithm" is the contract: parse the
  existing `thresholds:` map into `prev_user`, overlay the new
  default map, override each known key from `prev_user`. Skipping
  this step and writing the new defaults verbatim is the most
  user-visible regression this skill can ship.
- **Silently dropping unknown keys.** If `prev_user` has a key not
  in `new_defaults` (e.g. a deprecated threshold), drop it AND emit
  a chat warning naming the dropped key. Silent drops break audit
  trails — the user needs to know their override no longer applies.
- **Type-validating with `Convert.ToInt32` instead of strict parse.**
  `agent_count_max: "three"` should fail validation, not coerce to
  some default. Use strict numeric parsing; on failure, keep the
  default and warn.

## Target-file selection

- **Writing to `AGENTS.md` when `.github/copilot-instructions.md`
  exists.** The order in step 2's target-file table is the spec:
  `.github/copilot-instructions.md` is the primary destination;
  `AGENTS.md` gets a stub pointer. The reverse only happens during
  the explicit migration path (existing block in AGENTS.md, none in
  copilot-instructions.md).
- **Writing to both files.** "Both have a block" is the abort path —
  the user must consolidate manually. Writing the rule prose into
  two files would split the source of truth and let the two copies
  drift on the next update.
- **Creating `.github/` outside the project root.** If neither file
  exists, create `.github/copilot-instructions.md` under the resolved
  project root (the directory containing the `.sln`/`.slnx`/`*.AppHost.csproj`).
  Never write to a parent directory or a sibling project.

## Path safety

- **Following symlinks out of the project root.** Step 2's "Path
  safety" rule requires resolving the absolute path AND ensuring it
  still starts with the project-root prefix after symlink resolution.
  Some CI environments place repos under symlinks; a naive resolve
  can end up writing to the symlink target outside the workspace.
- **Accepting `..` in paths.** Reject any path containing `..`
  segments before normalization, unless the post-normalization path
  is still inside the project root. The simplest safe check:
  `Path.GetFullPath(target).StartsWith(Path.GetFullPath(projectRoot))`.

## Cross-tool stub on AGENTS.md

- **Re-adding the stub on every run.** The stub is one line:
  `> Agentic-perf rules for this project live in .github/copilot-instructions.md (managed by configure-agentic-perf-rules).`
  Check whether that exact line is already present before appending;
  re-running the skill should not grow the file by one line each time.
- **Replacing user prose in AGENTS.md with the stub.** AGENTS.md
  often contains real onboarding prose the user wrote. Append the
  stub at the bottom if missing; never overwrite existing content.

## Version handling

- **Refusing to downgrade is correct.** If the file has a newer
  version than this skill, abort cleanly with a chat message naming
  both versions. Do not "merge" or "convert" — that's how data loss
  happens.
- **Comparing versions as strings.** "v0.1.10" sorts before "v0.1.2"
  lexically. Parse into semver triples and compare numerically.

## Idempotency

- **No-op path must actually be a no-op.** When the block is present,
  same version, and structurally valid, the skill must not touch the
  file at all — not even to rewrite identical content. Tooling and
  git status both rely on "no change on second run". Verify by
  comparing the SHA256 hash before/after; they should match exactly.
- **Tracking "already installed" silently.** Even on a no-op, the
  chat output should say "configure-agentic-perf-rules v0.1.0 block
  already current — no changes". Quiet no-ops make the user think
  the skill didn't run.
