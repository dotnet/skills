# Button Group

## Table of Contents
1. [SfButtonGroup Overview](#sfbuttongroup-overview)
2. [Selection Modes](#selection-modes)
3. [Vertical Orientation](#vertical-orientation)
4. [Button Component](#button-component)
5. [Button Name and Value Properties](#button-name-and-value-properties)
6. [Button IsToggle Property](#button-istoggle-property)
7. [IconCss and Icons](#iconcss-and-icons)
8. [CssClass and Styling](#cssclass-and-styling)
9. [Disabled State](#disabled-state)
10. [Toggle Button](#toggle-button)
11. [Single Selection Pattern](#single-selection-pattern)
12. [Multiple Selection Pattern](#multiple-selection-pattern)
13. [Real-World Use Cases](#real-world-use-cases)

## SfButtonGroup Overview

The `SfButtonGroup` component groups multiple buttons together, supporting single or multiple selection modes. It's perfect for:
- Toggle groups
- Radio button alternatives
- Formatting toolbars
- Filter collections
- Option selectors

### Basic Button Group
```razor
<SfButtonGroup>
    <Button>Left</Button>
    <Button>Center</Button>
    <Button>Right</Button>
</SfButtonGroup>
```

Each button in the group is a `Button` child component.

### Group with Default Selection
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button>One</Button>
    <Button>Two</Button>
    <Button>Three</Button>
</SfButtonGroup>
```

## Selection Modes

### Single Selection
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button>Light</Button>
    <Button>Dark</Button>
    <Button>Auto</Button>
</SfButtonGroup>
```

With `Single` mode:
- Only one button can be selected at a time
- Clicking a button deselects the previously selected button
- Useful for mutually exclusive options

### Multiple Selection
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button>Bold</Button>
    <Button>Italic</Button>
    <Button>Underline</Button>
</SfButtonGroup>
```

With `Multiple` mode:
- Multiple buttons can be selected simultaneously
- Each button toggles independently
- Useful for combined options

### No Selection (Default)
```razor
<!-- Default: Buttons without selection tracking -->
<SfButtonGroup>
    <Button SelectedChanged="@(v => DoAction1(v))">Action 1</Button>
    <Button SelectedChanged="@(v => DoAction2(v))">Action 2</Button>
</SfButtonGroup>

@code {
    private void DoAction1(bool selected) { /* ... */ }
    private void DoAction2(bool selected) { /* ... */ }
}
```

With default `SelectionMode.None`:
- Buttons work as a styled group without automatic selection state management
- Click events still fire normally
- Use `SelectedChanged` callback to track state manually if needed

### SfButtonGroup Properties

| Property | Type | Purpose | Default |
|----------|------|---------|---------|
| `Mode` | SelectionMode | Selection behavior (None, Single, Multiple) | None |
| `IsVertical` | bool | Vertical layout arrangement | false |
| `CssClass` | string | Custom CSS classes | "" |
| `HtmlAttributes` | Dictionary<string, object> | Additional HTML attributes | {} |
| `Created` | EventCallback<object> | Lifecycle event after render | - |

### Group with HTML Attributes
```razor
<SfButtonGroup style="padding: 16px; background: #f5f5f5;">
    <Button>Option 1</Button>
    <Button>Option 2</Button>
</SfButtonGroup>
```

### Group with Lifecycle Event
```razor
<SfButtonGroup Created="@OnGroupCreated">
    <Button>Left</Button>
    <Button>Right</Button>
</SfButtonGroup>

@code {
    private void OnGroupCreated(object args)
    {
        Console.WriteLine("ButtonGroup has been created");
    }
}
```

## Vertical Orientation

The `IsVertical` property stacks buttons vertically instead of horizontally. This is useful for sidebar navigation, vertical toolbars, or option lists.

### Basic Vertical Group
```razor
<SfButtonGroup IsVertical="true">
    <Button>Option 1</Button>
    <Button>Option 2</Button>
    <Button>Option 3</Button>
</SfButtonGroup>
```

### Vertical with Single Selection
```razor
<SfButtonGroup IsVertical="true" Mode="@SelectionMode.Single">
    <Button Selected="@(selectedSize == "Small")" SelectedChanged="@(v => OnSizeSelected(v, "Small"))">Small</Button>
    <Button Selected="@(selectedSize == "Medium")" SelectedChanged="@(v => OnSizeSelected(v, "Medium"))">Medium</Button>
    <Button Selected="@(selectedSize == "Large")" SelectedChanged="@(v => OnSizeSelected(v, "Large"))">Large</Button>
</SfButtonGroup>

@code {
    private string selectedSize = "Medium";

    private void OnSizeSelected(bool selected, string size)
    {
        if (selected)
            selectedSize = size;
    }
}
```

### Vertical with Multiple Selection
```razor
<SfButtonGroup IsVertical="true" Mode="@SelectionMode.Multiple">
    <Button Selected="@activeOptions.Contains("Report")" SelectedChanged="@(v => OnOptionToggled("Report", v))">Generate Report</Button>
    <Button Selected="@activeOptions.Contains("Export")" SelectedChanged="@(v => OnOptionToggled("Export", v))">Export Data</Button>
    <Button Selected="@activeOptions.Contains("Print")" SelectedChanged="@(v => OnOptionToggled("Print", v))">Print</Button>
</SfButtonGroup>

<p>Selected Actions: @string.Join(", ", activeOptions)</p>

@code {
    private List<string> activeOptions = new();

    private void OnOptionToggled(string option, bool selected)
    {
        if (selected)
            activeOptions.Add(option);
        else
            activeOptions.Remove(option);
    }
}
```

## Button Component

### Basic Button
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button>Option A</Button>
    <Button>Option B</Button>
</SfButtonGroup>
```

### Button Component Properties

| Property | Type | Purpose | Default |
|----------|------|---------|---------|
| `Content` | string | Button text | "" |
| `IconCss` | string | Icon CSS classes | "" |
| `IconPosition` | IconPosition | Icon placement (Left, Right, Top, Bottom) | Left |
| `CssClass` | string | Custom CSS classes | "" |
| `Disabled` | bool | Enable/disable state | false |
| `Selected` | bool | Current selection state | false |
| `SelectedChanged` | EventCallback<bool> | Selection changed event | - |
| `Name` | string | Name attribute for underlying input | "" |
| `Value` | string | Value attribute for form submission | "" |
| `IsToggle` | bool | Enable toggle behavior | false |
| `HtmlAttributes` | Dictionary<string, object> | Additional HTML attributes (internal use) | {} |

### Complete Example
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IconCss="e-icons e-bold" CssClass="e-primary">Bold</Button>
    <Button IconCss="e-icons e-italic" CssClass="e-info">Italic</Button>
    <Button IconCss="e-icons e-underline" CssClass="e-success">Underline</Button>
    <Button IconCss="e-icons e-strikethrough" CssClass="e-warning" Disabled="true">Strikethrough</Button>
</SfButtonGroup>
```

### Button with Name and Value for Form Submission
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Name="priority" Value="low">Low Priority</Button>
    <Button Name="priority" Value="medium">Medium Priority</Button>
    <Button Name="priority" Value="high">High Priority</Button>
</SfButtonGroup>
```

### Button Name and Value Properties

The `Name` and `Value` properties allow buttons within a group to function as radio button alternatives for form submission.

| Property | Type | Purpose |
|----------|------|---------|
| `Name` | string | Groups buttons as radio inputs (same name = same group) |
| `Value` | string | The value submitted when this button is selected |

**Example: Priority Selection Form**
```razor
<EditForm Model="@formModel" OnValidSubmit="HandleSubmit">
    <SfButtonGroup Mode="@SelectionMode.Single">
        <Button Name="priority" Value="low">Low</Button>
        <Button Name="priority" Value="medium">Medium</Button>
        <Button Name="priority" Value="high">High</Button>
    </SfButtonGroup>
    <SfButton Type="ButtonType.Submit" Content="Submit" IsPrimary="true" />
</EditForm>

@code {
    private FormModel formModel = new();

    private void HandleSubmit(EditContext editContext)
    {
        Console.WriteLine($"Selected priority: {editContext.Model}");
    }

    public class FormModel
    {
        public string Priority { get; set; } = "medium";
    }
}
```

### Button IsToggle Property

The `IsToggle` property enables a button to act as a two-state toggle button. When enabled, the button visually toggles between selected and unselected states.

| Property | Type | Purpose | Default |
|----------|------|---------|---------|
| `IsToggle` | bool | Enable toggle behavior | false |

**Example: Toggle Button States**
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IsToggle="true" 
            Selected="@isBold" 
            SelectedChanged="@(v => isBold = v)"
            CssClass="@(isBold ? "e-primary" : "e-outline")">
        Bold
    </Button>
</SfButtonGroup>

@code {
    private bool isBold;
}
```

**Note:** When using `IsToggle`, you must:
1. Manually track the `Selected` state in your code
2. Sync via `SelectedChanged` callback
3. Apply appropriate CSS classes based on the selected state

## IconCss and Icons

The `IconCss` property allows you to add icons to buttons using CSS icon classes. You can use icon-only buttons, icons with text, or combine icons with other styling options for visual communication.

### Icon-Only Buttons
```razor
<h4>Icon-Only Button Group</h4>
<SfButtonGroup>
    <Button IconCss="e-icons e-bold" aria-label="Bold"></Button>
    <Button IconCss="e-icons e-italic" aria-label="Italic"></Button>
    <Button IconCss="e-icons e-underline" aria-label="Underline"></Button>
</SfButtonGroup>
```

**Best Practice**: Always include an `aria-label` on icon-only buttons for accessibility.

### Icon with Text
```razor
<h4>Icon with Text</h4>
<SfButtonGroup>
    <Button IconCss="e-icons e-align-left">Left</Button>
    <Button IconCss="e-icons e-align-center">Center</Button>
    <Button IconCss="e-icons e-align-right">Right</Button>
</SfButtonGroup>
```

### Icon with Position
```razor
<SfButtonGroup>
    <Button IconCss="e-icons e-save" IconPosition="IconPosition.Left">Save</Button>
    <Button IconCss="e-icons e-edit" IconPosition="IconPosition.Right">Edit</Button>
    <Button IconCss="e-icons e-upload" IconPosition="IconPosition.Top">Upload</Button>
    <Button IconCss="e-icons e-download" IconPosition="IconPosition.Bottom">Download</Button>
</SfButtonGroup>
```

### Formatting Toolbar with Icons
```razor
<h4>Text Formatting Toolbar</h4>
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IconCss="e-icons e-bold" Selected="@activeFormats.Contains("Bold")" SelectedChanged="@(v => OnFormatToggled("Bold", v))" aria-label="Bold"></Button>
    <Button IconCss="e-icons e-italic" Selected="@activeFormats.Contains("Italic")" SelectedChanged="@(v => OnFormatToggled("Italic", v))" aria-label="Italic"></Button>
    <Button IconCss="e-icons e-underline" Selected="@activeFormats.Contains("Underline")" SelectedChanged="@(v => OnFormatToggled("Underline", v))" aria-label="Underline"></Button>
    <Button IconCss="e-icons e-strikethrough" Selected="@activeFormats.Contains("Strikethrough")" SelectedChanged="@(v => OnFormatToggled("Strikethrough", v))" aria-label="Strikethrough"></Button>
</SfButtonGroup>

@code {
    private List<string> activeFormats = new();

    private void OnFormatToggled(string format, bool selected)
    {
        if (selected)
            activeFormats.Add(format);
        else
            activeFormats.Remove(format);
    }
}
```

### Icon Colors and Styling
```razor
<h4>Colored Icons with CssClass</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button IconCss="e-icons e-save" CssClass="e-primary" Selected="true">Save</Button>
    <Button IconCss="e-icons e-close" CssClass="e-danger">Close</Button>
    <Button IconCss="e-icons e-undo" CssClass="e-warning">Undo</Button>
    <Button IconCss="e-icons e-check" CssClass="e-success">Done</Button>
</SfButtonGroup>
```

### Common Icon Classes Reference

Common icon classes available for use (these are examples):
- **Text Formatting**: `e-bold`, `e-italic`, `e-underline`, `e-strikethrough`
- **Alignment**: `e-align-left`, `e-align-center`, `e-align-right`, `e-justify`
- **Navigation**: `e-home`, `e-user`, `e-settings`, `e-help`
- **Actions**: `e-save`, `e-close`, `e-undo`, `e-redo`, `e-check`, `e-delete`
- **Media**: `e-play`, `e-pause`, `e-stop`

## CssClass and Styling

The `CssClass` property allows you to apply custom CSS classes to individual buttons for styling, theming, and visual hierarchy. You can combine multiple classes for different effects.

### Semantic Color Classes
```razor
<SfButtonGroup>
    <Button CssClass="e-primary">Primary</Button>
    <Button CssClass="e-success">Success</Button>
    <Button CssClass="e-warning">Warning</Button>
    <Button CssClass="e-info">Info</Button>
    <Button CssClass="e-danger">Danger</Button>
</SfButtonGroup>
```

### Flat Button Style
```razor
<SfButtonGroup>
    <Button CssClass="e-flat">Day</Button>
    <Button CssClass="e-flat">Week</Button>
    <Button CssClass="e-flat">Month</Button>
</SfButtonGroup>
```

### Outlined Button Style
```razor
<SfButtonGroup>
    <Button CssClass="e-outline">Day</Button>
    <Button CssClass="e-outline">Week</Button>
    <Button CssClass="e-outline">Month</Button>
</SfButtonGroup>
```

### Rounded Corners
```razor
<SfButtonGroup CssClass="e-round-corner">
    <Button>Option 1</Button>
    <Button>Option 2</Button>
    <Button>Option 3</Button>
</SfButtonGroup>
```

### Mixed Styling in Single Group
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button CssClass="e-primary" Selected="@selected1" SelectedChanged="@(v => OnToggle(v, 1))">Primary Action</Button>
    <Button CssClass="e-success e-flat" Selected="@selected2" SelectedChanged="@(v => OnToggle(v, 2))">Success Action</Button>
    <Button CssClass="e-warning e-outline" Selected="@selected3" SelectedChanged="@(v => OnToggle(v, 3))">Warning Action</Button>
</SfButtonGroup>

@code {
    private bool selected1, selected2, selected3;

    private void OnToggle(bool value, int buttonNumber)
    {
        switch (buttonNumber)
        {
            case 1:
                selected1 = value;
                break;
            case 2:
                selected2 = value;
                break;
            case 3:
                selected3 = value;
                break;
        }
    }
}
```

### Custom CSS Classes
```razor
<style>
    .custom-highlight {
        background-color: #f0f0f0;
        border-radius: 8px;
        padding: 12px 16px;
    }

    .custom-active {
        font-weight: bold;
    }

    @@media (max-width: 768px) {
        .custom-highlight {
            border-radius: 4px;
            padding: 8px 12px;
            font-size: 0.9em;
        }
    }
</style>

<SfButtonGroup>
    <Button CssClass="custom-highlight">Button 1</Button>
    <Button CssClass="custom-highlight custom-active">Button 2</Button>
    <Button CssClass="custom-highlight">Button 3</Button>
</SfButtonGroup>
```

## Disabled State

The `Disabled` property disables individual buttons or an entire button group. Disabled buttons cannot be interacted with and are visually distinct.

### Mixed Enabled and Disabled Buttons
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button>Enabled</Button>
    <Button Disabled="true">Disabled</Button>
    <Button>Enabled</Button>
</SfButtonGroup>
```

### All Buttons Disabled
```razor
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Disabled="true">Option 1</Button>
    <Button Disabled="true">Option 2</Button>
    <Button Disabled="true">Option 3</Button>
</SfButtonGroup>
```

## Toggle Button

The `IsToggle` property enables individual buttons within a group to act as toggle buttons, allowing them to switch between selected and unselected states.

**Note:** When using `IsToggle` with `SelectionMode.Multiple` or `SelectionMode.Single`, you typically need to manually track the `Selected` state and sync via `SelectedChanged` callback. The `IsToggle` property enables the toggle behavior, but you control the visual selected state through `CssClass`.

### Toggle Button in Group
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IsToggle="true" 
            Selected="@isBold" 
            SelectedChanged="@(v => OnToggleChanged("Bold", v))"
            CssClass="@(isBold ? "e-primary" : "e-outline")">
        Bold
    </Button>
    <Button IsToggle="true" 
            Selected="@isItalic" 
            SelectedChanged="@(v => OnToggleChanged("Italic", v))"
            CssClass="@(isItalic ? "e-primary" : "e-outline")">
        Italic
    </Button>
    <Button IsToggle="true" 
            Selected="@isUnderline" 
            SelectedChanged="@(v => OnToggleChanged("Underline", v))"
            CssClass="@(isUnderline ? "e-primary" : "e-outline")">
        Underline
    </Button>
</SfButtonGroup>

@code {
    private bool isBold;
    private bool isItalic;
    private bool isUnderline;

    private void OnToggleChanged(string style, bool selected)
    {
        switch (style)
        {
            case "Bold":
                isBold = selected;
                break;
            case "Italic":
                isItalic = selected;
                break;
            case "Underline":
                isUnderline = selected;
                break;
        }
    }
}
```

### Icon Toggle Buttons
```razor
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IsToggle="true" 
            IconCss="e-icons e-bold" 
            Selected="@isBoldIcon" 
            SelectedChanged="@(v => isBoldIcon = v)"
            aria-label="Bold">
    </Button>
    <Button IsToggle="true" 
            IconCss="e-icons e-italic" 
            Selected="@isItalicIcon" 
            SelectedChanged="@(v => isItalicIcon = v)"
            aria-label="Italic">
    </Button>
    <Button IsToggle="true" 
            IconCss="e-icons e-underline" 
            Selected="@isUnderlineIcon" 
            SelectedChanged="@(v => isUnderlineIcon = v)"
            aria-label="Underline">
    </Button>
</SfButtonGroup>

@code {
    private bool isBoldIcon;
    private bool isItalicIcon;
    private bool isUnderlineIcon;
}
```

## Single Selection Pattern

### Radio Button Replacement
```razor
<h4>Choose Theme:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Selected="@(selectedTheme == "Light")" SelectedChanged="@(v => OnThemeSelected(v, "Light"))">Light</Button>
    <Button Selected="@(selectedTheme == "Dark")" SelectedChanged="@(v => OnThemeSelected(v, "Dark"))">Dark</Button>
    <Button Selected="@(selectedTheme == "System")" SelectedChanged="@(v => OnThemeSelected(v, "System"))">System</Button>
</SfButtonGroup>

<p style="background: @(selectedTheme == "Dark" ? "#333" : "#fff"); 
          color: @(selectedTheme == "Dark" ? "white" : "black");
          padding: 16px;">
    Current Theme: @selectedTheme
</p>

@code {
    private string selectedTheme = "Light";

    private void OnThemeSelected(bool selected, string theme)
    {
        if (selected)
            selectedTheme = theme;
    }
}
```

### Size Selection
```razor
<h4>Select Size:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Selected="@(selectedSize == "S")" SelectedChanged="@(v => OnSizeSelected(v, "S"))">S</Button>
    <Button Selected="@(selectedSize == "M")" SelectedChanged="@(v => OnSizeSelected(v, "M"))">M</Button>
    <Button Selected="@(selectedSize == "L")" SelectedChanged="@(v => OnSizeSelected(v, "L"))">L</Button>
    <Button Selected="@(selectedSize == "XL")" SelectedChanged="@(v => OnSizeSelected(v, "XL"))">XL</Button>
</SfButtonGroup>

<p>Selected Size: @selectedSize (@GetSizeDescription(selectedSize))</p>

@code {
    private string selectedSize = "M";

    private void OnSizeSelected(bool selected, string size)
    {
        if (selected)
            selectedSize = size;
    }

    private string GetSizeDescription(string size)
    {
        return size switch
        {
            "S" => "Small",
            "M" => "Medium",
            "L" => "Large",
            "XL" => "Extra Large",
            _ => "Unknown"
        };
    }
}
```

## Multiple Selection Pattern

### Formatting Toolbar
```razor
<h4>Text Formatting:</h4>
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button IconCss="e-icons e-bold" Selected="@activeFormats.Contains("Bold")" SelectedChanged="@(v => OnFormatToggled("Bold", v))">Bold</Button>
    <Button IconCss="e-icons e-italic" Selected="@activeFormats.Contains("Italic")" SelectedChanged="@(v => OnFormatToggled("Italic", v))">Italic</Button>
    <Button IconCss="e-icons e-underline" Selected="@activeFormats.Contains("Underline")" SelectedChanged="@(v => OnFormatToggled("Underline", v))">Underline</Button>
</SfButtonGroup>

<div style="@GetTextStyle()">
    Preview Text with Applied Formatting
</div>

@code {
    private List<string> activeFormats = new();

    private void OnFormatToggled(string format, bool selected)
    {
        if (selected)
            activeFormats.Add(format);
        else
            activeFormats.Remove(format);
    }

    private string GetTextStyle()
    {
        var style = "";
        if (activeFormats.Contains("Bold"))
            style += "font-weight: bold; ";
        if (activeFormats.Contains("Italic"))
            style += "font-style: italic; ";
        if (activeFormats.Contains("Underline"))
            style += "text-decoration: underline; ";
        return style;
    }
}
```

### Filter Collection
```razor
<h4>Filter Posts:</h4>
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button Selected="@activeCategories.Contains("Tech")" SelectedChanged="@(v => OnFilterChanged("Tech", v))">Tech</Button>
    <Button Selected="@activeCategories.Contains("Travel")" SelectedChanged="@(v => OnFilterChanged("Travel", v))">Travel</Button>
    <Button Selected="@activeCategories.Contains("Food")" SelectedChanged="@(v => OnFilterChanged("Food", v))">Food</Button>
    <Button Selected="@activeCategories.Contains("Sports")" SelectedChanged="@(v => OnFilterChanged("Sports", v))">Sports</Button>
</SfButtonGroup>

<h4>Posts:</h4>
<ul>
    @foreach (var post in GetFilteredPosts())
    {
        <li>@post.Title (@post.Category)</li>
    }
</ul>

@code {
    private List<string> activeCategories = new();

    private List<Post> allPosts = new()
    {
        new() { Title = "AI Trends", Category = "Tech" },
        new() { Title = "Paris Guide", Category = "Travel" },
        new() { Title = "Italian Food", Category = "Food" },
        new() { Title = "World Cup", Category = "Sports" },
    };

    private void OnFilterChanged(string category, bool selected)
    {
        if (selected)
            activeCategories.Add(category);
        else
            activeCategories.Remove(category);
    }

    private List<Post> GetFilteredPosts()
    {
        if (activeCategories.Count == 0)
            return allPosts;

        return allPosts.Where(p => activeCategories.Contains(p.Category)).ToList();
    }

    private class Post
    {
        public string Title { get; set; }
        public string Category { get; set; }
    }
}
```

## Real-World Use Cases

### Sort Options
```razor
<h4>Sort By:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Selected="@(currentSort == "Date")" SelectedChanged="@(v => OnSortChanged(v, "Date"))">Date</Button>
    <Button Selected="@(currentSort == "Name")" SelectedChanged="@(v => OnSortChanged(v, "Name"))">Name</Button>
    <Button Selected="@(currentSort == "Rating")" SelectedChanged="@(v => OnSortChanged(v, "Rating"))">Rating</Button>
</SfButtonGroup>

@code {
    private string currentSort = "Date";

    private void OnSortChanged(bool selected, string sortOption)
    {
        if (selected)
        {
            currentSort = sortOption;
            // Apply sorting logic here
        }
    }
}
```

### View Mode Toggle
```razor
<h4>View:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button IconCss="e-icons e-list-unordered" Selected="@(viewMode == "List")" SelectedChanged="@(v => OnViewModeChanged(v, "List"))">List</Button>
    <Button IconCss="e-icons e-grid-view" Selected="@(viewMode == "Grid")" SelectedChanged="@(v => OnViewModeChanged(v, "Grid"))">Grid</Button>
</SfButtonGroup>

@code {
    private string viewMode = "List";
    
    private void OnViewModeChanged(bool selected, string mode)
    {
        if (!selected) return;

        viewMode = mode;
    }
}
```

### Alignment Buttons
```razor
<h4>Text Alignment:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button IconCss="e-icons e-align-left" Selected="@(alignment == "Left")" SelectedChanged="@(v => OnAlignmentChanged(v, "Left"))">Left</Button>
    <Button IconCss="e-icons e-align-center" Selected="@(alignment == "Center")" SelectedChanged="@(v => OnAlignmentChanged(v, "Center"))">Center</Button>
    <Button IconCss="e-icons e-align-right" Selected="@(alignment == "Right")" SelectedChanged="@(v => OnAlignmentChanged(v, "Right"))">Right</Button>
    <Button IconCss="e-icons e-justify" Selected="@(alignment == "Justify")" SelectedChanged="@(v => OnAlignmentChanged(v, "Justify"))">Justify</Button>
</SfButtonGroup>

@code {
    private string alignment = "Left";
    
    private void OnAlignmentChanged(bool selected, string newAlignment)
    {
        if (!selected) return;

        alignment = newAlignment;
    }
}
```

### Time Range Selection
```razor
<h4>Select Period:</h4>
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Selected="@isToday" SelectedChanged="@(v => OnPeriodChanged(v, "Today"))">Today</Button>
    <Button Selected="@isWeek" SelectedChanged="@(v => OnPeriodChanged(v, "Week"))">Week</Button>
    <Button Selected="@isMonth" SelectedChanged="@(v => OnPeriodChanged(v, "Month"))">Month</Button>
    <Button Selected="@isYear" SelectedChanged="@(v => OnPeriodChanged(v, "Year"))">Year</Button>
</SfButtonGroup>

<p>Showing data for: @selectedPeriod</p>

@code {
    private string selectedPeriod = "Week";
    private bool isToday, isWeek = true, isMonth, isYear;

    private void OnPeriodChanged(bool selected, string period)
    {
        if (!selected) return;

        selectedPeriod = period;
        // Fetch data for selected period
        isToday = period == "Today";
        isWeek = period == "Week";
        isMonth = period == "Month";
        isYear = period == "Year";
    }
}
```

## Edge Cases and Gotchas

### Gotcha 1: No Initial Selection with Single Mode
```razor
<!-- Issue: User might not realize they need to select something -->
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button>Option 1</Button>
    <Button>Option 2</Button>
</SfButtonGroup>

<!-- Better: Document expected behavior or track selection with handlers -->
<SfButtonGroup Mode="@SelectionMode.Single">
    <Button Selected="@isOpt1" SelectedChanged="@(v => OnSelect(v, 1))">Option 1</Button>
    <Button Selected="@isOpt2" SelectedChanged="@(v => OnSelect(v, 2))">Option 2</Button>
</SfButtonGroup>

@code {
    private int selectedOption = 0;
    private bool isOpt1 = true, isOpt2;

    private void OnSelect(bool selected, int option)
    {
        if (!selected) return;

        selectedOption = option;
		// One will always be "selected" through your tracking
        isOpt1 = option == 1;
        isOpt2 = option == 2;
    }
}
```

### Gotcha 2: Multiple Selection Confusion
```razor
<!-- Confusing: No visual indicator that multiple selections are possible -->
<SfButtonGroup Mode="@SelectionMode.Multiple">
    <Button>A</Button>
    <Button>B</Button>
</SfButtonGroup>

<!-- Better: Include visual indicator or documentation -->
<div>
    <p>Select one or more options:</p>
    <SfButtonGroup Mode="@SelectionMode.Multiple">
        <Button Selected="@selectedOptions.Contains("A")" SelectedChanged="@(v => ToggleSelection("A", v))">A</Button>
        <Button Selected="@selectedOptions.Contains("B")" SelectedChanged="@(v => ToggleSelection("B", v))">B</Button>
    </SfButtonGroup>
    <p>Selected: @string.Join(", ", selectedOptions)</p>
</div>

@code {
    private List<string> selectedOptions = new();

    private void ToggleSelection(string option, bool selected)
    {
        if (selected)
            selectedOptions.Add(option);
        else
            selectedOptions.Remove(option);
    }
}
```
