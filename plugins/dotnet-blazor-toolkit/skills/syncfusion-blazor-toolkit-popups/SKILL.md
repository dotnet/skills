---
license: MIT
name: syncfusion-blazor-toolkit-popups
description: >
  Decide which Syncfusion Blazor Toolkit overlay component fits the
  requirement and route to the right sub-skill.
  USE FOR: choosing between SfDialog (modal blocking) and SfTooltip (hover
  help) when both could plausibly fit, picking the right OpensOn mode for
  tooltip triggers, understanding the focus / aria-modal / role differences
  between the two, and reviewing the cross-cutting decision matrix.
  DO NOT USE FOR: implementing a dialog (load syncfusion-blazor-toolkit-dialog
  instead), implementing a tooltip (load syncfusion-blazor-toolkit-tooltip
  instead), or building non-blocking progress overlays (use
  syncfusion-blazor-toolkit-notifications).
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit: Popups (Router)

The two popup components have very different UX semantics and the most common
bug in this area is misusing one for the other. This router hands off to the
right sub-skill; for the **decision matrix** (lifecycle, ARIA, anti-patterns)
read the link below.

## Quick Routing

| Requirement | Component | Load |
|---|---|---|
| Confirmation modal (Yes/No) | `SfDialog` | [dialog/SKILL.md](dialog/SKILL.md) |
| Form-in-modal (login, register, settings) | `SfDialog` | [dialog/SKILL.md](dialog/SKILL.md) |
| Alert dialog ("Saved", "Error", "Confirm delete") | `SfDialog` | [dialog/SKILL.md](dialog/SKILL.md) |
| Modal driven by a service from many call sites | `SfDialog` + `DialogService` | [dialog/SKILL.md](dialog/SKILL.md) |
| Help text on an icon button | `SfTooltip` | [tooltip/SKILL.md](tooltip/SKILL.md) |
| Keyboard-shortcut hint ("Ctrl+S") | `SfTooltip` | [tooltip/SKILL.md](tooltip/SKILL.md) |
| Brief status / validation copy on hover | `SfTooltip` | [tooltip/SKILL.md](tooltip/SKILL.md) |
| HTML / rich content shown on hover | `SfTooltip` + `ContentTemplate` | [tooltip/SKILL.md](tooltip/SKILL.md) |

## Cross-Component Decision Matrix

📄 **Read:** [router/dialog-vs-tooltip-decision-matrix.md](router/dialog-vs-tooltip-decision-matrix.md)

**Read this when:**
- You're not sure which component to use
- You're reviewing someone else's code
- You're refactoring a `SfDialog` that is too heavyweight for its purpose
  (or vice-versa)

It covers lifecycle, ARIA, animation parity, common anti-patterns, and a
decision cheat-sheet.

## Sub-Skills

- [syncfusion-blazor-toolkit-dialog](dialog/SKILL.md) — `SfDialog`
- [syncfusion-blazor-toolkit-tooltip](tooltip/SKILL.md) — `SfTooltip`

## Cross-References to Other Toolkit Skills

- Spinner overlay (full-page / region / modal-blocking): load
  **syncfusion-blazor-toolkit-notifications**
- Button triggers (`<SfButton>` is the most common tooltip/dialog target):
  load **syncfusion-blazor-toolkit-buttons**
- Form inputs inside a dialog (`<EditForm>` validation, etc.):
  load **syncfusion-blazor-toolkit-inputs**

## Don'ts

- Don't use `SfDialog` for a hover-help tooltip — use `SfTooltip` instead
- Don't use `SfTooltip` for a confirmation — use `SfDialog`
- Don't import both `SfDialog` and `SfTooltip` namespaces into the same
  component when you only need one — clarity over completeness

## Next Steps

After reading the decision matrix:
- Building a modal? Open [dialog/SKILL.md](dialog/SKILL.md).
- Building hover help? Open [tooltip/SKILL.md](tooltip/SKILL.md).