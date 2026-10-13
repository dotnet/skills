# Theme and Host Files Reference

Toolkit components require CSS theming. Use the host file for your app type and link the Fluent stylesheet there. From Toolkit 2.0, component styles use Blazor CSS isolation and the shared theme layer is emitted at render time by `SfThemeRoot`.

> **Framework support.** `Syncfusion.Blazor.Toolkit` 2.0.0 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, the package may restore successfully, but Toolkit assemblies are not available to the application, so the build later fails with errors such as `CS0246: The type or namespace name 'Syncfusion' could not be found`. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

> **Never replace the whole host file.** The examples below show only the lines to add to the template-generated host file. Keep `HeadOutlet`, `Routes`, the render-mode directive on `Routes`, and the existing script tags (`_framework/blazor.web.js`, `_framework/blazor.webassembly.js`, or `blazor.server.js`) exactly as the template generated them.

## Blazor Web App

**Host file**: `Components/App.razor`

**Theme**: Fluent is the default Toolkit stylesheet. The package also includes `highcontrast.min.css`. Do not link commercial theme files such as Bootstrap, Tailwind, or Material.

**Add this line to the existing `<head>`** in `Components/App.razor` (do not replace the file):
```razor
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

**Legacy Blazor Server note**: If you are on an older Server template, `_Host.cshtml` may still be the host file and `blazor.server.js` may still appear there. Prefer `App.razor` and `blazor.web.js` for current Blazor Web App templates. The same single-line addition applies — link only, do not replace `_Host.cshtml`.

## Standalone Blazor WebAssembly

**Host file**: `wwwroot/index.html`

**Theme**: Same stylesheets as the Web App host. Link `fluent.min.css` unless the user asks for high contrast.

**Add this line to the existing `<head>`** in `wwwroot/index.html` (do not replace the file):
```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

## Theme delivery model (2.0)

- **Host stylesheet** (`fluent.min.css` / `highcontrast.min.css`): still linked in the host file as above.
- **Shared theme layer**: `SfThemeRoot` writes a single `<style id="sf-theme-root">` element containing `:root` design tokens, base/utility styles, the icon font, keyframes, and dark / high-contrast / forced-colors rules. Every consumer-facing root Toolkit component renders `<SfThemeRoot />` as the previous sibling of its root element. Only one owner per Blazor renderer emits the style; ownership recovers on dispose. No consumer configuration is required when Toolkit components are present. Place an explicit `<SfThemeRoot />` only when theme emission is needed without a Toolkit component in the tree.
- **Component styles**: migrated to Blazor CSS isolation (`.razor.css` → scoped bundle). Selectors and cascade may differ from the 1.x global stylesheet. App-level overrides that targeted unscoped toolkit class names may need the `::deep` combinator or other isolation-aware adjustments.

## Available stylesheets

The stylesheets live under `_content/Syncfusion.Blazor.Toolkit/styles/`. Verified against package version 2.0.0:

- `fluent.min.css` — default Fluent stylesheet. Use this unless the user asks for something else.
- `highcontrast.min.css` — high-contrast stylesheet.

Prefer `fluent.min.css` for all components. Do not link commercial theme files (`bootstrap5`, `tailwind`, `material`, and similar). They are not part of this package and will not style Toolkit components.

## Local References

Use the local reference:
```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

Local references are preferred because they:
- Avoid external dependencies
- Work offline
- Are bundled with your application

## Common Mistakes

1. **Replacing the whole host file**: If `HeadOutlet`, `Routes`, the render-mode directive on `Routes`, or the framework script tag (`blazor.web.js`, `blazor.webassembly.js`, `blazor.server.js`) is removed while adding the stylesheet link, events and routing stop working. Restore the template and add only the `<link>` line.
2. **Linking in the wrong file**: Use `_Host.cshtml` for legacy Blazor Server, `Components/App.razor` for a Blazor Web App, or `wwwroot/index.html` for standalone WebAssembly.
3. **Wrong path directory**: The path is `_content/Syncfusion.Blazor.Toolkit/styles/` (`styles/`, not `themes/`).
4. **Wrong stylesheet name**: `bootstrap5`, `tailwind`, and `material` files are not in this package. Use `fluent.min.css` or `highcontrast.min.css`.
5. **Missing `.min` extension**: The filenames are `fluent.min.css` and `highcontrast.min.css`, not `fluent.css`.
6. **Missing link entirely**: Without the theme, components render unstyled and may be hard to see.
7. **1.x custom CSS no longer applies**: After CSS isolation, overrides that targeted unscoped class names may need `::deep` or scope adjustments.
