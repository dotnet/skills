# Theme and Host Files Reference

From Toolkit **2.0**, theming no longer requires linking `fluent.min.css` or `highcontrast.min.css` in the host file. Component styles use Blazor CSS isolation; the shared theme layer is emitted at render time by `SfThemeRoot`.

> **Framework support.** `Syncfusion.Blazor.Toolkit` 2.0.1 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, NuGet restore fails with an incompatibility such as `NU1202` because the package has no compatible assets for those TFMs. Do not expect a later `CS0246` as the primary signal—fix the framework mismatch first. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

> **Do not replace the host file.** Keep `HeadOutlet`, `Routes`, the render-mode directive on `Routes`, the app's own styles link (`app.css` / `{Project}.styles.css`), and the framework script tags exactly as the template generated them.

## Theme delivery model (2.0)

Two tiers — neither requires a Toolkit theme `<link>` in the host:

1. **Shared theme layer (`SfThemeRoot`)**
   Writes a single `<style id="sf-theme-root">` element containing `:root` design tokens, base/utility styles, the icon font, keyframes, and dark / high-contrast / forced-colors rules.
   Every consumer-facing root Toolkit component renders `<SfThemeRoot />` as the **previous sibling** of its root element. Only one owner per Blazor renderer emits the style; ownership recovers on dispose.
   **No consumer configuration** is required when Toolkit components are present. Place an explicit `<SfThemeRoot />` only when theme emission is needed without a Toolkit component in the tree. A JavaScript fallback can inject the theme into `document.head` only when no owner exists for that renderer.

2. **Component styles (CSS isolation)**
   Migrated to Blazor CSS isolation (`.razor.css` → scoped bundle). Styles ship with the NuGet package and are applied through the isolation / static-web-assets pipeline. Selectors and cascade may differ from the 1.x global stylesheet. App-level overrides that targeted unscoped toolkit class names may need the `::deep` combinator or other isolation-aware adjustments.

## What the host file should contain

Ensure the host has an active `<link>` to `{Project}.styles.css` so Toolkit's isolated component styles load. Empty standalone WebAssembly templates leave this link commented out, so uncomment it or add it if missing. `SfThemeRoot` does not replace the application's scoped CSS bundle.

Keep the **template-generated** stylesheet links (examples):

- Blazor Web App: `Components/App.razor` — typically `app.css` and `{Project}.styles.css` (or `@Assets[...]` equivalents).
- Standalone WebAssembly: `wwwroot/index.html` — typically `css/app.css` and the project styles bundle.
- Legacy Server: `_Host.cshtml` — template CSS links only.

**Do not add** any of the following for Toolkit 2.0:

```html
<!-- 1.x pattern — no longer required -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
<link href="_content/Syncfusion.Blazor.Toolkit/styles/highcontrast.min.css" rel="stylesheet" />
<link href="_content/Syncfusion.Blazor.Toolkit/styles/button.min.css" rel="stylesheet" />
```

When migrating from 1.x, **remove** those Toolkit theme `<link>` tags. Leaving them is usually harmless if the files still exist in the package, but they are not part of the 2.0 setup path and the skill must not instruct users to add them.

## High contrast and dark

High-contrast and dark rules are part of the shared layer emitted by `SfThemeRoot` (and forced-colors support). Do not switch themes by swapping `fluent.min.css` / `highcontrast.min.css` host links.

## Common Mistakes

1. **Adding a Toolkit theme `<link>` from 1.x docs** — not required in 2.0; rely on `SfThemeRoot` + CSS isolation.
2. **Replacing the whole host file** while "fixing" theming — can drop `HeadOutlet`, `Routes`, or the framework script and break the app.
3. **Removing the app's own `{Project}.styles.css` / `app.css` link** — those remain required for the application and for scoped CSS.
4. **Assuming unstyled components mean a missing `fluent.min.css`** — first check package install, `AddSyncfusionBlazorToolkit()`, root `@using Syncfusion.Blazor.Toolkit`, and that a Toolkit component (or explicit `<SfThemeRoot />`) is in the tree.
5. **1.x custom CSS no longer applies** — after CSS isolation, overrides that targeted unscoped class names may need `::deep` or scope adjustments.
