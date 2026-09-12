# Theme and Host Files Reference

Toolkit components require CSS theming. Use the host file for your app type and link the Fluent stylesheet there.

## Blazor Web App / Modern Blazor Server

**Host file**: `App.razor`

**Theme**: Syncfusion Blazor Toolkit supports **Fluent theme only**.

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

## Blazor WebAssembly

**Host file**: `index.html` (in the `wwwroot` folder)

**Theme**: Syncfusion Blazor Toolkit supports **Fluent theme only**.

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

## Available Themes

Syncfusion Blazor Toolkit supports **only the Fluent theme**:

- `fluent.min.css` — Microsoft Fluent Design theme (the only supported theme) — use this for the common stylesheet
- Individual component stylesheets: `button.min.css`, `calendar.min.css`, `chart.min.css`, `checkbox.min.css`, `dialog.min.css`, `dropdown.min.css`, `input.min.css`, `spinner.min.css`, `textbox.min.css`, `tooltip.min.css`, etc.

Do not attempt to use other theme CSS files (bootstrap5, tailwind, material, etc.) with the Toolkit; they are not supported and will not work correctly.

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

1. **Linking in the wrong file**: Link the theme in the app's host file (`App.razor` for Blazor Web App / modern Blazor Server; `wwwroot/index.html` for Blazor WebAssembly).
2. **Wrong path directory**: Ensure the path is `_content/Syncfusion.Blazor.Toolkit/styles/` exactly (note: `styles/`, not `themes/`).
3. **Wrong theme name**: Using `bootstrap5.min.css`, `tailwind.min.css`, `material.min.css`, or other non-Fluent themes won't work. Toolkit supports **only `fluent.min.css`**.
4. **Missing .min extension**: Ensure you use `.min.css` (not just `.css`). The correct filename is `fluent.min.css`.
5. **Missing link entirely**: Without the theme, components render unstyled and may be hard to see.
6. **Individual stylesheet only**: If you link only individual component stylesheets (e.g., just `button.min.css`), components not included will be unstyled. Use `fluent.min.css` for all components unless performance optimization is needed.
