# Theme and Host Files Reference

Toolkit components require CSS theming. Use the host file for your app type and link the Fluent stylesheet there.

> **Framework support.** The Toolkit targets `net8.0`, `net9.0`, and `net10.0`. If `<TargetFramework>` is older, recommend upgrading before installing: restore succeeds on `net7.0` and earlier but supplies no assemblies, so the build fails with `CS0246`.

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

## Available stylesheets

The stylesheets live under `_content/Syncfusion.Blazor.Toolkit/styles/`. Verified against package version 1.0.2:

- `fluent.min.css` — default Fluent stylesheet. Use this unless the user asks for something else.
- `highcontrast.min.css` — high-contrast stylesheet.
- Per-component files: `button`, `buttongroup`, `calendar`, `chart`, `checkbox`, `datepicker`, `datetimepicker`, `dialog`, `input`, `numerictextbox`, `popup`, `radio-button`, `spinner`, `switch`, `textarea`, `textbox`, `timepicker`, `tooltip`, `uploader` (each with the `.min.css` suffix).

There is no `dropdown.min.css`. Prefer `fluent.min.css`, which styles every component **except** `SfNumericTextBox`: its `.e-numerictextbox` selectors are in neither `fluent.min.css` nor `highcontrast.min.css`, so also link `numerictextbox.min.css` when that component is used. If a per-component file is linked, confirm it exists in the installed version's `styles/` folder.

Do not link commercial theme files (`bootstrap5`, `tailwind`, `material`, and similar). They are not part of this package and will not style Toolkit components.

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
7. **Per-component stylesheet only**: If you link only individual component stylesheets (e.g., just `button.min.css`), components not included will be unstyled. Use `fluent.min.css` for all components unless performance optimization is needed, and verify any per-component file you link actually exists in the package's `styles/` directory for the installed version.

