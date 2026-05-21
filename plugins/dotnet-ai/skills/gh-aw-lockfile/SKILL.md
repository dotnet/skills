---
name: gh-aw-lockfile
description: "Automatically detect and compile stale GitHub Agentic Workflow lock files. USE FOR: editing .github/workflows/*.md or .github/agents/*.agent.md files, creating new agentic workflows, updating workflow frontmatter (triggers, permissions, safe-outputs, imports), fixing 'ERR_CONFIG: Lock file is outdated' errors. Ensures .lock.yml files are always regenerated and committed alongside .md source changes. DO NOT USE FOR: non-agentic workflow .yml files, standard GitHub Actions workflows, repositories without gh-aw infrastructure. INVOKES: gh aw compile --strict, git add, git status."
license: MIT
---

# Agentic Workflow Lock File Management

When editing GitHub Agentic Workflow `.md` files, the compiled `.lock.yml` files **must** be regenerated and committed in the same change. Forgetting this causes `ERR_CONFIG: Lock file is outdated` failures at runtime.

## When This Skill Activates

Activate whenever you modify any of these files:
- `.github/workflows/*.md` (agentic workflow sources)
- `.github/workflows/shared/*.md` (shared workflow imports)
- `.github/agents/*.agent.md` (agent definitions)

Also activate when you see this error:
```
ERR_CONFIG: Lock file '...' is outdated!
The workflow file '...' frontmatter has changed.
Run 'gh aw compile' to regenerate the lock file.
```

## Required Steps

### 1. After editing any `.md` workflow source

Identify which workflows were modified:

```bash
# Find modified workflow .md files
git diff --name-only | grep -E '\.github/workflows/.*\.md$|\.github/agents/.*\.md$'
```

### 2. Compile the lock files

For **each** modified workflow, run:

```bash
gh aw compile <workflow-id> --strict
```

Where `<workflow-id>` is the filename without extension. Examples:

```bash
# If you edited .github/workflows/build-failure-analysis.md:
gh aw compile build-failure-analysis --strict

# If you edited .github/workflows/daily-qa.md:
gh aw compile daily-qa --strict

# If you edited a shared import (e.g., shared/build-failure-analysis-shared.md),
# compile ALL workflows that import it:
gh aw compile build-failure-analysis --strict
gh aw compile build-failure-analysis-command --strict
```

### 3. Compile all (when unsure which are affected)

If a shared file or agent definition was changed and you're unsure which workflows import it:

```bash
# Compile all agentic workflows in the repo
gh aw compile --strict
```

### 4. Commit the lock files

Always commit the regenerated `.lock.yml` files in the **same commit** as the `.md` changes:

```bash
git add .github/workflows/*.lock.yml .github/aw/actions-lock.json
git commit  # include with the .md changes
```

## Rules

- **ALWAYS** compile in strict mode (`--strict`). Strict mode is the default unless a workflow's frontmatter sets `strict: false` — **never** add `strict: false`.
- **NEVER** hand-edit `.lock.yml` files. They are auto-generated.
- **ALWAYS** commit `.lock.yml` changes in the same PR as the `.md` source changes.
- If `gh aw` is not installed, install it: `gh extension install github/gh-aw`

## Common Mistakes

| Mistake | Result | Fix |
|---|---|---|
| Edit `.md`, forget to compile | `ERR_CONFIG` at runtime | `gh aw compile <id> --strict` |
| Edit shared import, compile only one consumer | Other consumers have stale locks | `gh aw compile --strict` (all) |
| Hand-edit `.lock.yml` | Diverges from source, breaks on next compile | Delete lock, recompile from `.md` |
| Add `strict: false` to frontmatter | Bypasses safety checks | Remove it, use `--strict` |

## Detecting Stale Lock Files

To check if any lock files are stale without compiling:

```bash
# List workflow .md files and their lock files
for f in .github/workflows/*.md; do
  id=$(basename "$f" .md)
  lock=".github/workflows/${id}.lock.yml"
  if [ ! -f "$lock" ]; then
    echo "MISSING: $lock (source: $f)"
  fi
done
```
