# Themes, icons, and accessibility

## Gate theme APIs

Preserve existing theme and startup policy. Application.SetColorMode was experimental in .NET 9
and became non-experimental in .NET 10. Check the project's target and experimental opt-ins
before using it; a new opt-in is not an incidental UI fix.

System colors adapt to the configured color mode, but hard-coded palettes, owner drawing,
DataGridView painting, and native children need actual theme/contrast checks. Do not promise
that setting the application's color mode themes every control or third-party component.

## Command icons

New ordinary command ToolStrips need semantic icons unless explicitly requested as text-only
headers or a domain where icons do not make sense. Preserve text, shortcuts, tooltips, and
accessible names; icons alone do not make commands accessible. Do not restyle an unrelated
working toolbar during a narrow bug fix.

Without a UI library, use a small owned image factory backed by installed Windows symbol fonts:
Segoe Fluent Icons when available, Segoe UI Symbol, then Segoe MDL2 Assets. Check installation
and semantic glyph availability; private-use code points are not interchangeable across fonts.
Do not assume Segoe Fluent Icons is installed on every Windows 10 machine.

If the project already uses WARP or another library, verify and use that library's actual symbol
API rather than duplicating its factory. This skill does not require that library or its skills.

Keep logical user font preference separate from the observed live Font: under PerMonitorV2
WinForms can expose an already DPI-scaled font. Either scale a stable logical font preference
by current DPI once, or measure the live native Font without adding another DPI multiplier.
Keep rendered raster size and ImageScalingSize consistent, regenerate after relevant changes,
and dispose replaced owned images. Apply the same policy to menu/status/tree/list images.

## Date/time and specialized entry

For new LOB entry requiring explicit formatting, prefer masked date/time/currency/measurement
editors rather than defaulting to native calendar controls. This is a scoped editor choice,
not proof that DateTimePicker or MonthCalendar is universally inaccessible or unthemeable.
Do not replace existing native controls outside the requested scope.

Before asserting a native-control accessibility or dark-mode defect:

1. Record the Windows build, target/runtime version, control type, configured color mode,
   culture, DPI/font, and assistive technology.
2. Reproduce the actual keyboard, focus, UI Automation/screen-reader, or contrast failure.
3. Compare the proposed replacement under the same conditions. Masked entry is not automatically
   more accessible: verify mask announcements, errors, culture-specific formats, caret movement,
   selection, and complete keyboard operation.
4. Record observed results, not a universal ban inferred from one old runtime or screenshot.
   If this cannot be tested, state the limitation and make only a conditional recommendation.

## Verify usability

Check TabIndex, keyboard-only traversal, unambiguous mnemonics, meaningful accessible names and
descriptions, default focus, and accept/cancel behavior. Exercise long localized text, resizing,
font growth, and relevant DPI/theme combinations. State which screen-reader/contrast checks
actually ran; compilation is not accessibility evidence.

## Sources

- [.NET 9 WinForms dark-mode introduction](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net90)
- [.NET 10 integrated dark-mode support](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100)
