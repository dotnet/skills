---
name: "MSBuild Quality Review"
description: >-
  Reviews changed MSBuild project and infrastructure files for correctness,
  incremental-build behavior, item and property semantics, target ordering,
  package extension authoring, maintainability, and credible performance issues.

on:
  # Execute the workflow and generated lock from the trusted base branch. The
  # agent never checks out or executes pull request code; it reads the PR diff
  # and changed files through the restricted, read-only GitHub tools.
  pull_request_target:
    types: [opened, synchronize, reopened, ready_for_review]
    paths:
      - "**/*.csproj"
      - "**/*.fsproj"
      - "**/*.vbproj"
      - "**/*.proj"
      - "**/*.projitems"
      - "**/*.props"
      - "**/*.targets"
      - "**/*.tasks"
      - "**/*.overridetasks"
      - "**/*.nuspec"
      - "**/Directory.Build.*"
      - "**/Directory.Packages.*"
  # Only trusted authors may trigger this agent: with pull_request_target it
  # runs from the base branch with Copilot auth and copilot-requests: write.
  # gh-aw v1.2.2 rejects roles: all for that combination.
  roles: [admin, maintainer, write]

if: github.event.pull_request.draft == false

checkout: false

concurrency:
  group: msbuild-quality-review-${{ github.event.pull_request.number }}
  cancel-in-progress: true

permissions:
  contents: read
  pull-requests: read
  copilot-requests: write

env:
  MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS: ${{ vars.MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS }}

imports:
  # Body and safe-output configuration are provided by
  # msbuild-quality-review-shared.md. Detailed, redistributable guidance is
  # installed at .github/agents/msbuild-quality-reviewer.agent.md.
  - msbuild-quality-review-shared.md

inlined-imports: true
strict: true
timeout-minutes: 20
---

Review the changed MSBuild project and infrastructure files in the triggering pull request and follow the shared review guidance.
