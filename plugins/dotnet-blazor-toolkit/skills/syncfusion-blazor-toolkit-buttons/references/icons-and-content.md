# Icons and Content

## Table of Contents
1. [Icon CSS Classes](#icon-css-classes)
2. [Icon Positioning](#icon-positioning)
3. [Syncfusion Icon Library](#syncfusion-icon-library)
4. [Content vs ChildContent](#content-vs-childcontent)
5. [Complex Content Patterns](#complex-content-patterns)
6. [SVG and Custom Icons](#svg-and-custom-icons)
7. [Icon-Only Buttons](#icon-only-buttons)

## Icon CSS Classes

### Basic Icon Button
```razor
<SfButton IconCss="e-icons e-plus" Content="Add" />
```

The `IconCss` property accepts space-separated CSS class names for icon styling:

- First class typically identifies the icon library (`e-icons`)
- Second class specifies the icon (`e-plus`, `e-save`, `e-close`, `e-refresh` etc.)

### Icon Library Classes

| Library | Class | Usage |
|---------|-------|-------|
| Syncfusion | `e-icons` | Built-in Syncfusion icon set |
| Font Awesome | `fa fa-*` | Font Awesome icon library |
| Bootstrap | `bi bi-*` | Bootstrap icon library |

### Common Syncfusion Icons

| Icon | Class |
|------|-------|
| Add | `e-icons e-plus` |
| Edit | `e-icons e-edit` |
| Delete | `e-icons e-delete` |
| Save | `e-icons e-save` |
| Close | `e-icons e-close` |
| Search | `e-icons e-search` |
| Filter | `e-icons e-filter` |
| Refresh | `e-icons e-refresh` |

## Icon Positioning

### Default Values
| Property | Default Value |
|----------|---------------|
| `IconCss` | `""` (empty string) |
| `IconPosition` | `IconPosition.Left` |

### Position Enum Values
The `IconPosition` parameter accepts:
- `Left` — Icon before text (default)
- `Right` — Icon after text
- `Top` — Icon above text
- `Bottom` — Icon below text

### Examples by Position

#### Left Position
```razor
<SfButton IconCss="e-icons e-edit" 
          IconPosition="IconPosition.Left" 
          Content="Edit Item" />
```
Renders: **[icon] Edit Item**

#### Right Position
```razor
<SfButton IconCss="e-icons e-arrow-right" 
          IconPosition="IconPosition.Right" 
          Content="Next" />
```
Renders: **Next [icon]**

#### Top Position
```razor
<SfButton IconCss="e-icons e-download" 
          IconPosition="IconPosition.Top" 
          Content="Download" />
```
Renders: Icon above text

#### Bottom Position
```razor
<SfButton IconCss="e-icons e-upload" 
          IconPosition="IconPosition.Bottom" 
          Content="Upload" />
```
Renders: Icon below text

### Choosing Position

**Left position** — Best for action buttons (Edit, Delete, Add)
```razor
<SfButton IconCss="e-icons e-edit" IconPosition="IconPosition.Left" Content="Edit" />
<SfButton IconCss="e-icons e-delete" IconPosition="IconPosition.Left" Content="Delete" />
```

**Right position** — Best for navigation/direction (Next, Previous, Continue)
```razor
<SfButton IconCss="e-icons e-arrow-right" IconPosition="IconPosition.Right" Content="Next" />
```

**Top/Bottom** — Best for mobile or icon-focused layouts
```razor
<SfButton IconCss="e-icons e-download" 
          IconPosition="IconPosition.Top" 
          Content="Download" 
          Style="width: 80px;" />
```

## Syncfusion Icon Library

### Icon Library Integration
The Syncfusion icon library is included with theme CSS. To use icons:

1. Ensure theme CSS is imported (contains icon font)
2. Use `e-icons` class with specific icon class
3. Icons render as font-based glyphs (scalable)

### Complete Icon Examples
```razor
<div style="display: flex; gap: 8px; flex-wrap: wrap;">
    <SfButton IconCss="e-icons e-plus" Content="Add" />
    <SfButton IconCss="e-icons e-edit" Content="Edit" />
    <SfButton IconCss="e-icons e-delete" Content="Delete" />
    <SfButton IconCss="e-icons e-save" Content="Save" />
    <SfButton IconCss="e-icons e-search" Content="Search" />
    <SfButton IconCss="e-icons e-refresh" Content="Refresh" />
    <SfButton IconCss="e-icons e-settings" Content="Settings" />
</div>
```

## Content vs ChildContent

### Content Property (Recommended)

The `Content` property is the **recommended** way to set button text content.

```razor
<!-- Simple text strings -->
<SfButton Content="Simple Button" />
<SfButton Content="@buttonLabel" />
```

Use `Content` for:
- Simple text labels
- Dynamic text from variables
- Simple expressions

**Example: Content with Dynamic Values**
```razor
<SfButton Content="@(isLoading ? "Processing..." : "Submit")" />
<SfButton Content="@GetButtonText()" />
```

### ⚠️ Warning: ChildContent is Internal API

> **⚠️ Critical:** `ChildContent` is marked `[EditorBrowsable(EditorBrowsableState.Never)]` in the source code, indicating it's a **hidden/internal API**. Using it means you're relying on an unsupported API that could break in future releases without notice. For simple text content, **always prefer the `Content` property instead.**

The `ChildContent` parameter allows complex HTML content but should be avoided:

```razor
<!-- ChildContent allows HTML elements (but this is an internal API) -->
<SfButton>
    <strong>Bold</strong> and <em>Italic</em>
</SfButton>
```

ChildContent allows:
- HTML elements and tags
- Complex formatting
- Nested components
- Mixed text and elements

### When to Use Each

| Approach | Use Case | Status |
|----------|----------|--------|
| `Content` property | Simple text, dynamic text | ✅ Recommended |
| `ChildContent` | Complex HTML, icons with text | ⚠️ Internal API - Avoid |

### Practical Comparison

```razor
<!-- ✅ Content: Recommended for simple text -->
<SfButton Content="Click Me" />

<!-- ✅ Content: With icon using IconCss and Content properties (recommended) -->
<SfButton Content="Edit Item" IconCss="e-icons e-edit" IconPosition="IconPosition.Left" />

<!-- ❌ ChildContent: Complex content with HTML (internal API - avoid) -->
<SfButton>
    <span class="custom-style">
        <strong>Advanced</strong> Button
    </span>
</SfButton>

<!-- ❌ ChildContent: Icon + Text (internal API - avoid) -->
<SfButton>
    <i class="e-icons e-edit" style="margin-right: 4px;"></i>
    Edit This Item
</SfButton>
```

**Best Practice:** For buttons with icons and text, use the `Content` and `IconCss` properties together:

```razor
<SfButton Content="Edit" IconCss="e-icons e-edit" IconPosition="IconPosition.Left" />
```

## Complex Content Patterns

### Icon with Styled Text
```razor
<SfButton>
    <i class="e-icons e-download" style="margin-right: 6px; font-weight: bold;"></i>
    <span style="text-decoration: underline;">Download Report</span>
</SfButton>
```

### Badge with Button
```razor
<SfButton>
    Notifications
    <span style="background: red; color: white; border-radius: 10px; 
                 padding: 2px 6px; margin-left: 6px; font-size: 10px;">
        3
    </span>
</SfButton>
```

### Multi-line Button Content
```razor
<SfButton Style="white-space: normal; height: auto; padding: 12px;">
    <div>
        <strong>Click Me</strong>
        <small style="display: block; color: #666; margin-top: 4px;">
            Additional description goes here
        </small>
    </div>
</SfButton>
```

### Status Indicator Button
```razor
<SfButton Style="position: relative;">
    <div style="display: flex; align-items: center; gap: 8px;">
        <span style="display: inline-block; width: 8px; height: 8px; 
                     background: @(isActive ? "green" : "gray"); 
                     border-radius: 50%;"></span>
        @(isActive ? "Online" : "Offline")
    </div>
</SfButton>

@code {
    private bool isActive = true;
}
```

## SVG and Custom Icons

### Using SVG Icons
```razor
<SfButton>
    <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 1C4.13401 1 1 4.13401 1 8C1 11.866 4.13401 15 8 15C11.866 15 15 11.866 15 8C15 4.13401 11.866 1 8 1Z"/>
    </svg>
    SVG Icon Button
</SfButton>
```

### Font Awesome Icons
```razor
<!-- Ensure Font Awesome CSS is included -->
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.0.0/css/all.min.css" />

<SfButton>
    <i class="fas fa-heart"></i>
    Favorite
</SfButton>
```

### Bootstrap Icons
```razor
<!-- Ensure Bootstrap Icons CSS is included -->
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.7.2/font/bootstrap-icons.css" />

<SfButton>
    <i class="bi bi-download"></i>
    Download
</SfButton>
```

## Icon-Only Buttons

### Icon Without Text
```razor
<!-- Icon only - no Content property -->
<SfButton IconCss="e-icons e-edit" title="Edit" />
<SfButton IconCss="e-icons e-delete" title="Delete" />
<SfButton IconCss="e-icons e-save" title="Save" />
```

Always include `title` attribute for accessibility with icon-only buttons.

### Icon Button Sizing
```razor
<!-- Standard size -->
<SfButton IconCss="e-icons e-edit" title="Edit" />

<!-- Larger icon button -->
<SfButton IconCss="e-icons e-edit" title="Edit" 
          CssClass="e-large" 
          Style="width: 40px; height: 40px;" />

<!-- Small icon button -->
<SfButton IconCss="e-icons e-edit" title="Edit" 
          CssClass="e-small" 
          Style="width: 24px; height: 24px;" />
```

### Icon Button Group
```razor
<div style="display: flex; gap: 4px;">
    <SfButton IconCss="e-icons e-bold" title="Bold" />
    <SfButton IconCss="e-icons e-italic" title="Italic" />
    <SfButton IconCss="e-icons e-underline" title="Underline" />
</div>
```

### Icon Toolbar Pattern
```razor
<div style="display: flex; gap: 8px; padding: 8px; background: #f5f5f5; border-radius: 4px;">
    <SfButton IconCss="e-icons e-cut" title="Cut" />
    <SfButton IconCss="e-icons e-copy" title="Copy" />
    <SfButton IconCss="e-icons e-paste" title="Paste" />
    <span style="border-right: 1px solid #ccc;"></span>
    <SfButton IconCss="e-icons e-undo" title="Undo" />
    <SfButton IconCss="e-icons e-redo" title="Redo" />
</div>
```

## Edge Cases and Gotchas

### Gotcha 1: Correct Character Encoding in Icon Classes
```razor
<!-- Wrong: Uses Cyrillic е (U+0435) instead of ASCII e (U+0065) -->
<SfButton IconCss="e-icons е-plus" />

<!-- Correct: Uses ASCII e (U+0065) in both parts -->
<SfButton IconCss="e-icons e-plus" />
```

### Gotcha 2: Icon Library Must Be Loaded
```razor
<!-- Won't work: Icon library CSS not included -->
<SfButton IconCss="e-icons e-plus" />

<!-- Solution: Ensure theme CSS in _Host.cshtml -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" /> -->
```

### Gotcha 3: ChildContent Replaces Content
```razor
<!-- Icon position doesn't apply with ChildContent -->
<SfButton IconCss="e-icons e-edit" 
          IconPosition="IconPosition.Left" 
          Content="Edit">
    <!-- This ChildContent replaces Content and IconCss -->
    Custom Content Here
</SfButton>

<!-- Correct: Use ChildContent with icon -->
<SfButton>
    <i class="e-icons e-edit"></i>
    Edit
</SfButton>
```

### Gotcha 4: Accessibility with Icons
```razor
<!-- Bad: Icon-only button without label -->
<SfButton IconCss="e-icons e-delete" />

<!-- Good: Include title or aria-label -->
<SfButton IconCss="e-icons e-delete" 
          title="Delete Item" 
          aria-label="Delete this item" />
```
