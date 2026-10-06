# Theme and Host Files Reference

Toolkit components require CSS theming. Use the host file for your app type and link the Fluent stylesheet there.

## Blazor Web App

**Host file**: `Components/App.razor`

**Theme**: Fluent is the default Toolkit stylesheet. The package also includes `highcontrast.min.css`. Do not link commercial theme files such as Bootstrap, Tailwind, or Material.

**Example**:
```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="app.css" />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

**Legacy Blazor Server note**: If you are on an older Server template, `_Host.cshtml` may still be the host file and `blazor.server.js` may still appear there. Prefer `App.razor` and `blazor.web.js` for current Blazor Web App templates.

## Standalone Blazor WebAssembly

**Host file**: `wwwroot/index.html`

**Theme**: Same stylesheets as the Web App host. Link `fluent.min.css` unless the user asks for high contrast.

**Example**:
```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>MyApp</title>
    <base href="/" />
    <link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="app.css" />
</head>
<body>
    <div id="app"></div>
    <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

## Available stylesheets

- `fluent.min.css` — default Fluent stylesheet. Use this unless the user asks for something else.
- `highcontrast.min.css` — high-contrast stylesheet shipped with the Toolkit package.
- Individual component stylesheets: `button.min.css`, `calendar.min.css`, `chart.min.css`, `checkbox.min.css`, `dialog.min.css`, `dropdown.min.css`, `input.min.css`, `spinner.min.css`, `textbox.min.css`, `tooltip.min.css`, and others.

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

1. **Linking in the wrong file**: Use `_Host.cshtml` for legacy Blazor Server, `Components/App.razor` for a Blazor Web App, or `wwwroot/index.html` for standalone WebAssembly.
2. **Wrong path directory**: The path is `_content/Syncfusion.Blazor.Toolkit/styles/` (`styles/`, not `themes/`).
3. **Wrong stylesheet name**: `bootstrap5`, `tailwind`, and `material` files are not in this package. Use `fluent.min.css` or `highcontrast.min.css`.
4. **Missing `.min` extension**: The filenames are `fluent.min.css` and `highcontrast.min.css`, not `fluent.css`.
5. **Missing link entirely**: Without the theme, components render unstyled and may be hard to see.
6. **Individual stylesheet only**: If you link only individual component stylesheets (e.g., just `button.min.css`), components not included will be unstyled. Use `fluent.min.css` for all components unless performance optimization is needed.
