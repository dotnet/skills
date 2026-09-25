---
name: "MSBuild Quality Review"
description: >-
  Reviews changed MSBuild project and infrastructure files for correctness,
  incremental-build behavior, item and property semantics, target ordering,
  package extension authoring, maintainability, and credible performance issues.

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]
    forks: ["*"]
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
      - "**/Directory.Build.*"
      - "**/Directory.Packages.*"
  roles: all

if: github.event.pull_request.draft == false

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
  - msbuild-quality-review-shared.md

inlined-imports: true
strict: true
timeout-minutes: 20
---

<!--
  Body and safe-output configuration are provided by
  msbuild-quality-review-shared.md. Detailed, redistributable MSBuild guidance
  is installed at .github/agents/msbuild-quality-reviewer.agent.md.
-->
