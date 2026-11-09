# Layout and dialog contracts

## Scope and sizing chain

Inventory affected Forms, modal Forms, and hosted views before choosing controls. Keep new
application surfaces designer-editable. New tab/wizard/view-part UserControls use
AutoScaleMode.Inherit with the containing Form as scaling root. Repair proven double scaling
in existing views; do not change a healthy Form's DPI policy just to match a template.

Use nested TableLayoutPanels for precise regions, not a single oversized grid or brittle
coordinates. Captions/single-line regions usually AutoSize; bounded expandable content uses
Percent sizing. Reserve Absolute sizing for genuinely fixed content. FlowLayoutPanel remains
appropriate for flowing content, not newly designed precise command-button rows.

Inspect AutoSize, AutoSizeMode, Dock, Margin, MinimumSize, MaximumSize, row/column styles,
and siblings from leaf to root. GroupBoxes in TLP cells should be Dock.Fill, AutoSize,
GrowAndShrink and contain one direct child TLP with the same sizing contract. Setting only
the innermost container to AutoSize does not repair a capped parent.

## Nested command-button row

For new or explicitly repaired horizontal OK/Cancel rows:

1. Use an outer and inner TableLayoutPanel, both AutoSize and GrowAndShrink.
2. Outer: two AutoSize rows; two columns, Percent 100 then AutoSize.
3. Put an empty AutoSize, Dock.Fill Label at row 0, column 1, with five logical pixels of top
   and bottom Margin. Let normal host scaling scale that design-time spacing.
4. Inner: one AutoSize row; one equal Percent column per button. Dock it Fill into outer
   row 1, column 1. Each button is Dock.Fill, AutoSize, GrowAndShrink.
5. Wrap captions over 30 characters at meaningful word boundaries. Use localized resources
   where the app does; do not hard-code English-only wrapping assumptions.
6. Place the outer panel in an AutoSize root row, spanning every root column. This decouples
   button widths from the data-entry column scheme.
7. For a fully autosized root, keep that new row AutoSize. For a resizable root, place a
   deliberate Percent spring/content region before the footer. Do not add a second competing
   Percent spring beside an existing expanding viewport.
8. If AutoSize content exceeds a bounded root, an empty spring can become negative and overlap
   the footer. Use one measured scrollable Percent viewport, with AutoSize header/footer outside
   it. Do not enlarge saved top-level bounds to hide the overflow.
9. For explicitly requested vertical top-positioned commands, transpose the dimensions while
   retaining nesting and width independence.

Verify equal widths, complete preferred-size chains, non-overlapping regions, nonnegative row
heights, reachable scrolled inputs, and keyboard-reachable actions after font/resize/text growth.
Do not rewrite a healthy existing layout merely to replace its containers with this recipe.

## Empty editor minima

Empty TextBox, MaskedTextBox, ComboBox, and NumericUpDown need capacity-based minima when an
autosized width would otherwise collapse. Reuse the project's helper, or traverse Controls
recursively with type matching; never use reflection to discover layout controls.

Measure expected characters using the settled live Font without a second DPI factor, then add
control chrome. A numeric range 1 through 100 needs three digit positions plus one for air;
include signs and decimal places for other ranges. Reserve the full mask and expected text
capacity. Replace MinimumSize.Width while preserving height; do not add the old minimum or
base the result on current user text. Treat matched composite primitive editors as leaves,
so NumericUpDown's private edit control does not acquire an independent minimum.

For reusable specialized editors, consider SetBoundsCore and GetPreferredSize only when needed
by the task. Preserve parsing, culture, keyboard interaction, and validation semantics.

## Dialog completion

Use AcceptButton/CancelButton and button DialogResult.OK/Cancel for simple modal completion.
Reject an invalid OK close in OnFormClosing with `e.Cancel = true`; Cancel must remain usable.
For staged/async validation keep DialogResult.None until success, expose busy/cancel/error
state, and do not close or commit the draft early. DataContext draft use is target-gated in
the API/startup reference; older targets retain typed draft/BindingSource contracts.

## Window geometry and font-only changes

For a new LOB main window without saved placement, the recommended default is centered outer
normal bounds approximately 75% of the chosen Screen.WorkingArea width and 80% of its height.
This excludes the taskbar and is not a mandatory size for tools, modal dialogs, or existing
user-restored windows. Content, accessibility, and explicit product requirements take precedence.

For new/restored placement logic, store normal bounds as monitor-local 96-DPI DIPs relative
to WorkingArea origin, with schema, monitor identity, session DPI/scaling, resolution/working
area, and maximized state. Finalize font and child scaling before converting/restoring with
the target's current DPI-awareness coordinate space; never add a font factor. Migrate old
pixel-only data explicitly, clamp to the current working area, and handle missing monitors.

A font-only transaction must preserve normal outer bounds and pixel MinimumSize/MaximumSize,
while child content grows or scrolls. SuspendLayout alone does not suppress autoscaling:
capture constraints/bounds, change font, settle scaling, restore constraints before bounds,
and verify after ResumeLayout. Do not suppress genuine OS-suggested DPI-transition bounds.
Capture normal placement separately before maximizing; do not guess an inverse-scaled
RestoreBounds. Real mixed-monitor/maximized checks remain distinct from formula/static tests.
