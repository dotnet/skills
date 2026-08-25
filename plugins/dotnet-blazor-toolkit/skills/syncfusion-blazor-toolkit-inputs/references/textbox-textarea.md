# TextBox and TextArea Components

## Table of Contents
- [SfTextBox Overview](#sftextbox-overview)
- [SfTextBox Properties](#sftextbox-properties)
- [SfTextBox Events](#sftextbox-events)
- [Floating Labels](#floating-labels)
- [Placeholders and Hints](#placeholders-and-hints)
- [Focus and Blur Handling](#focus-and-blur-handling)
- [Input Validation](#input-validation)
- [SfTextArea Component](#sftextarea-component)
- [Practical Examples](#practical-examples)

## SfTextBox Overview

**SfTextBox** is the fundamental single-line text input component. It provides a styled alternative to standard HTML input elements with additional features like floating labels, custom styling, and event handling.

**When to use SfTextBox:**
- General text input (names, usernames, search queries)
- Email, URL, or password fields
- Search/filter inputs
- Labels with floating animation
- Custom styling and themes

### Basic Implementation

```razor
<SfTextBox Placeholder="Enter your name"></SfTextBox>
```

### With Value Binding

```razor
<SfTextBox @bind-Value="@myText" Placeholder="Type something"></SfTextBox>

@code {
    private string myText = "";
}
```

## SfTextBox Properties

### Essential Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Value` | `string` | `""` | Current text value |
| `Placeholder` | `string` | `""` | Placeholder text when empty |
| `Type` | `InputType` | `Text` | Input type (Text, Email, Password, URL, Number) |
| `Autocomplete` | `AutoComplete` | `On` | Browser autocomplete behavior (On, Off) |
| `Disabled` | `bool` | `false` | Disable user input |
| `ReadOnly` | `bool` | `false` | Prevent modification |
| `CssClass` | `string` | `""` | Custom CSS classes |
| `Multiline` | `bool` | `false` | Allow multiple lines (for multi-line text) |

### Appearance Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FloatLabelType` | `FloatLabelType` | `Never` | Floating label mode (Never, Always, Auto) |
| `ShowClearButton` | `bool` | `false` | Display clear/reset button |
| `Width` | `string?` | `null` (100%) | Width of the TextBox component |
| `TabIndex` | `int` | `0` | Tab order of the TextBox |
| `HtmlAttributes` | `Dictionary<string, object>?` | `null` | Additional HTML attributes for wrapper element |
| `InputAttributes` | `Dictionary<string, object>` | `{}` | Additional HTML attributes for input element |
| `CssClass` | `string` | `""` | Custom CSS classes |

### Input Type Examples

```razor
@* Regular text input *@
<SfTextBox Type="InputType.Text" Placeholder="Text"></SfTextBox>

@* Email validation *@
<SfTextBox Type="InputType.Email" Placeholder="your@email.com"></SfTextBox>

@* Password field *@
<SfTextBox Type="InputType.Password" Placeholder="Enter password"></SfTextBox>

@* URL input *@
<SfTextBox Type="InputType.URL" Placeholder="https://example.com"></SfTextBox>

@* Phone number *@
<SfTextBox Type="InputType.Tel" Placeholder="(555) 123-4567"></SfTextBox>
```

### Autocomplete Property

The `Autocomplete` property controls the browser's built-in autocomplete suggestions:

```razor
@* Autocomplete enabled (default) *@
<SfTextBox Autocomplete="AutoComplete.On" Placeholder="Username"></SfTextBox>

@* Autocomplete disabled - suitable for sensitive fields *@
<SfTextBox Autocomplete="AutoComplete.Off" Type="InputType.Password" Placeholder="Password"></SfTextBox>
```

### SfTextBox Variants
The TextBox component offers two visual variants: `Filled`, and `Outlined`

```razor
<SfTextBox Placeholder="Filled TextBox" CssClass="e-filled" FloatLabelType="FloatLabelType.Auto" Width="250px"></SfTextBox>
<SfTextBox Placeholder="Outlined TextBox" CssClass="e-outlined" FloatLabelType="FloatLabelType.Auto"></SfTextBox>
```

### TextBox with Disabled State

```razor
<SfTextBox Disabled="true" Placeholder="Disabled text box"></SfTextBox>
<SfTextBox Disabled="true" @bind-Value="@disabledValue" Placeholder="Disabled with value"></SfTextBox>

@code {
    private string disabledValue = "Cannot be modified";
}
```

### TextBox with ReadOnly State

```razor
<SfTextBox ReadOnly="true" Placeholder="Read-only text box"></SfTextBox>
<SfTextBox ReadOnly="true" @bind-Value="@readonlyValue" Placeholder="Read-only with value"></SfTextBox>

@code {
    private string readonlyValue = "View only - cannot edit";
}
```

### Multi-line TextBox

Use the `Multiline` property for multi-line text input (textarea behavior):

```razor
<SfTextBox @bind-Value="@notes" Multiline="true" Placeholder="Enter your notes"></SfTextBox>

@code {
    private string notes = "";
}
```

## TextBox Public Methods

### AddIconAsync

Adds icons to the TextBox at the specified position (prepend or append):

```razor
<SfTextBox @ref="textBoxRef" Placeholder="Enter Date"></SfTextBox>
<button @onclick="AddDateIcon">Add Date Icon</button>

@code {
    private SfTextBox textBoxRef = new();

    private async Task AddDateIcon()
    {
        var iconEvents = new Dictionary<string, object>()
        {
            { "onclick", new Action(OnIconClick) }
        };
        await textBoxRef.AddIconAsync("prepend", "e-date-icon", iconEvents);
    }

    private void OnIconClick()
    {
        Console.WriteLine("Date icon clicked");
    }
}
```

### FocusAsync

Sets focus to the TextBox programmatically:

```razor
<SfTextBox @ref="textBoxRef" Placeholder="Enter your text"></SfTextBox>
<button @onclick="FocusTextBox">Focus Input</button>

@code {
    private SfTextBox textBoxRef = new();

    private async Task FocusTextBox()
    {
        await textBoxRef.FocusAsync();
    }
}
```

### FocusOutAsync

Removes focus from the TextBox:

```razor
<SfTextBox @ref="textBoxRef" Placeholder="Enter your text"></SfTextBox>
<button @onclick="BlurTextBox">Blur Input</button>

@code {
    private SfTextBox textBoxRef = new();

    private async Task BlurTextBox()
    {
        await textBoxRef.FocusOutAsync();
    }
}
```

## SfTextBox Events

### ValueChange Event

Fired when the text value changes:

```razor
<SfTextBox ValueChange="OnTextChanged"></SfTextBox>

@code {
    private void OnTextChanged(ChangedEventArgs args)
    {
        Console.WriteLine($"New value: {args.Value}");
        Console.WriteLine($"Previous value: {args.PreviousValue}");
    }
}
```

### Input Event

Real-time event firing on every keystroke:

```razor
<SfTextBox OnInput="@OnInputChange"></SfTextBox>

@code {
    private void OnInputChange(InputEventArgs args)
    {
        Console.WriteLine($"Character count: {args.Value?.Length}");
    }
}
```

### Focus and Blur Events

```razor
<SfTextBox 
    OnFocus="@OnFocus" 
    OnBlur="@OnBlur">
</SfTextBox>

@code {
    private void OnFocus(FocusInEventArgs args)
    {
        Console.WriteLine("TextBox focused");
    }

    private void OnBlur(FocusOutEventArgs args)
    {
        Console.WriteLine("TextBox blurred, value: " + args.Value);
    }
}
```

## Floating Labels

Floating labels improve UX by providing context when users interact with inputs. The label animates up when focused or filled.

### FloatLabelType Options

| Option | Behavior |
|--------|----------|
| `Never` | Label fixed at placeholder position |
| `Always` | Label always floats above input |
| `Auto` | Label floats when focused or has value |

### Implementation

```razor
<div class="form-group">
    <SfTextBox 
        @bind-Value="@email" 
        Placeholder="Email Address"
        FloatLabelType="FloatLabelType.Auto">
    </SfTextBox>
</div>

<div class="form-group">
    <SfTextBox 
        @bind-Value="@password" 
        Type="InputType.Password"
        Placeholder="Password"
        FloatLabelType="FloatLabelType.Auto">
    </SfTextBox>
</div>

@code {
    private string email = "";
    private string password = "";
}
```

## Placeholders and Hints

### Basic Placeholder

```razor
<SfTextBox Placeholder="Enter your full name"></SfTextBox>
```

### Placeholder with Helper Text

```razor
<div class="input-container">
    <SfTextBox Placeholder="example@domain.com"></SfTextBox>
    <small class="helper-text">We'll never share your email</small>
</div>
```

### Using ClearButton

```razor
<SfTextBox 
    @bind-Value="@searchText" 
    Placeholder="Search products..."
    ShowClearButton="true"
    ValueChange="@OnSearchChanged">
</SfTextBox>

@code {
    private string searchText = "";

    private void OnSearchChanged(ChangedEventArgs args)
    {
        Console.WriteLine($"Searching for: {args.Value}");
    }
}
```

## Focus and Blur Handling

### Focus Management

```razor
<SfTextBox @ref="textboxRef" Placeholder="Click or press Tab"></SfTextBox>

<button @onclick="FocusInput">Focus Input</button>
<button @onclick="BlurInput">Blur Input</button>

@code {
    private SfTextBox textboxRef;

    private async Task FocusInput()
    {
        await textboxRef.FocusAsync(); // Focus programmatically
    }

    private async Task BlurInput()
    {
        await textboxRef.FocusOutAsync(); // Blur programmatically
    }
}
```

### Focus Event with Validation

```razor
<SfTextBox 
    OnFocus="@OnInputFocused" 
    OnBlur="@OnInputBlurred"
    @bind-Value="@username">
</SfTextBox>

<p class="validation-message">@validationMessage</p>

@code {
    private string username = "";
    private string validationMessage = "";

    private void OnInputFocused(FocusInEventArgs args)
    {
        validationMessage = "Username must be 3-20 characters";
    }

    private void OnInputBlurred(FocusOutEventArgs args)
    {
        if (username.Length < 3)
        {
            validationMessage = "❌ Username too short";
        }
        else if (username.Length > 20)
        {
            validationMessage = "❌ Username too long";
        }
        else
        {
            validationMessage = "✓ Username valid";
        }
    }
}
```

## Input Validation

### Basic Validation

```razor
@using System.ComponentModel.DataAnnotations

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    
    <div class="form-group">
        <label>Email:</label>
        <SfTextBox 
            @bind-Value="@model.Email" 
            Type="InputType.Email"
            Placeholder="Enter email">
        </SfTextBox>
        <ValidationMessage For="@(() => model.Email)" />
    </div>

    <button type="submit">Submit</button>
</EditForm>

@code {
    private LoginModel model = new();

    private void HandleSubmit()
    {
        Console.WriteLine($"Email: {model.Email}");
    }

    public class LoginModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = "";
    }
}
```

### Custom Validation

```razor
<SfTextBox 
    @bind-Value="@username"
    OnBlur="@ValidateUsername"
    Placeholder="Username (3-20 chars)">
</SfTextBox>

<p class="error" style="color: red;">@usernameError</p>

@code {
    private string username = "";
    private string usernameError = "";

    private void ValidateUsername(FocusOutEventArgs args)
    {
        if (username.Length < 3)
        {
            usernameError = "Username too short (min 3 characters)";
        }
        else if (username.Length > 20)
        {
            usernameError = "Username too long (max 20 characters)";
        }
        else
        {
            usernameError = "";
        }
    }
}
```

### Real-time Character Count

```razor
<div class="input-group">
    <SfTextBox 
        @bind-Value="@comment"
        OnInput="@OnCommentInput"
        Placeholder="Enter your comment (max 200 chars)"
        InputAttributes="@MaxLengthAttrs">
    </SfTextBox>
    <p class="char-count">@comment.Length / 200 characters</p>
</div>

@code {
    private string comment = "";
    private Dictionary<string, object> MaxLengthAttrs => new() { { "maxlength", "200" } };

    private void OnCommentInput(InputEventArgs args)
    {
        // Character count updates in real-time
        Console.WriteLine($"Remaining: {200 - comment.Length}");
    }
}
```

## SfTextArea Component

**SfTextArea** is for multi-line text input. It's ideal for comments, descriptions, feedback, and long-form content.

### Basic TextArea

The `RowCount` property defines the initial number of visible text lines in the TextArea and `ColumnCount` property specifies the TextArea's initial width in terms of the approximate number of characters per line,

```razor
<SfTextArea 
    Placeholder="Enter your feedback..."
    RowCount="4"
    ColumnCount="50">
</SfTextArea>
```

### TextArea with Value Binding

```razor
<SfTextArea 
    @bind-Value="@feedback"
    Placeholder="Tell us what you think..."
    RowCount="5">
</SfTextArea>

<p>Characters entered: @feedback.Length</p>

@code {
    private string feedback = "";
}
```

### TextArea Properties

| Property | Type | Description |
|----------|------|-------------|
| `Placeholder` | `string` | Placeholder text |
| `Width` | `string?` | Width of the TextArea component |
| `RowCount` | `int` | Number of visible rows |
| `ColumnCount` | `int` | Number of visible columns |
| `MaxLength` | `int` | Maximum character length (-1 for unlimited) |
| `ResizeMode` | `Resize` | Resizing direction (Both, Horizontal, Vertical, None) |
| `Disabled` | `bool` | Disable input |
| `ReadOnly` | `bool` | Read-only mode |
| `TabIndex` | `int` | Tab order of the TextArea |
| `HtmlAttributes` | `Dictionary<string, object>?` | Additional HTML attributes for wrapper element |
| `InputAttributes` | `Dictionary<string, object>` | Additional HTML attributes for input element |

### Appearance Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FloatLabelType` | `FloatLabelType` | `Never` | Floating label mode (Never, Always, Auto) |

### TextArea with Resizing

The TextArea component offers flexible resizing options through the ResizeMode property, including `Both`, `Horizontal`, `Vertical`, and `None`. This option enables the TextArea to be resized in any direction.

```razor
<SfTextArea Placeholder="Both side resizable" ResizeMode="Resize.Both" FloatLabelType="FloatLabelType.Always"></SfTextArea>
```

### TextArea with Disabled State

```razor
<SfTextArea Disabled="true" Placeholder="Disabled text area"></SfTextArea>
<SfTextArea Disabled="true" @bind-Value="@disabledFeedback" Placeholder="Disabled with value" RowCount="3"></SfTextArea>

@code {
    private string disabledFeedback = "This cannot be modified";
}
```

### TextArea with ReadOnly State

```razor
<SfTextArea ReadOnly="true" @bind-Value="@termsText" Placeholder="Read-only text area" RowCount="4"></SfTextArea>

@code {
    private string termsText = "Terms and conditions: Lorem ipsum dolor sit amet, consectetur adipiscing elit...";
}
```

### TextArea with Variants

The TextArea component offers two visual variants — `Filled`, and `Outlined`

```razor
<SfTextArea Placeholder="Tell us a little bit about yourself..." CssClass="e-filled" Width="300px"></SfTextArea>
<SfTextArea Placeholder="Tell us a little bit about yourself..." CssClass="e-outlined" Width="300px"></SfTextArea>
```

### TextArea with Float Label

The floating label functionality supports three modes: `Never`, `Auto`, and `Always`.

```razor
<SfTextArea Placeholder="Never" FloatLabelType="FloatLabelType.Never" Width="300px"></SfTextArea>
<SfTextArea Placeholder="Always" FloatLabelType="FloatLabelType.Always" Width="300px"></SfTextArea>
```

### TextArea with MaxLength 

Use the `MaxLength` property to enforce a maximum character limit in the TextArea.

```razor
<SfTextArea Placeholder="Maximum length of 20 characters" MaxLength="20" FloatLabelType="FloatLabelType.Always" Width="300px"></SfTextArea>
```

### TextArea Events

The TextArea component supports the following events: `Created`, `OnInput`, `ValueChange`, `OnFocus`, and `OnBlur`.

#### Created Event

Fired when the TextArea component is initialized:

```razor
<SfTextArea Created="@OnCreated" Placeholder="Enter text"></SfTextArea>

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("TextArea component created");
    }
}
```

#### OnInput Event

Real-time event firing on every keystroke:

```razor
<SfTextArea OnInput="@OnTextInput" Placeholder="Type here..."></SfTextArea>

@code {
    private void OnTextInput(TextAreaInputEventArgs args)
    {
        Console.WriteLine($"Input value: {args.Value}");
        Console.WriteLine($"Character count: {args.Value?.Length ?? 0}");
    }
}
```

#### ValueChange Event

Fired when the text value changes:

```razor
<SfTextArea ValueChange="@OnValueChanged" Placeholder="Enter feedback"></SfTextArea>

@code {
    private void OnValueChanged(TextAreaValueChangeEventArgs args)
    {
        Console.WriteLine($"New value: {args.Value}");
        Console.WriteLine($"Previous value: {args.PreviousValue}");
    }
}
```

#### OnFocus Event

Fired when the TextArea receives focus:

```razor
<SfTextArea OnFocus="@OnFocus" Placeholder="Click here"></SfTextArea>

@code {
    private void OnFocus(TextAreaFocusInEventArgs args)
    {
        Console.WriteLine("TextArea focused");
        Console.WriteLine($"Event source: {args.Event}");
    }
}
```

#### OnBlur Event

Fired when the TextArea loses focus:

```razor
<SfTextArea OnBlur="@OnBlur" Placeholder="Click away to blur"></SfTextArea>

@code {
    private void OnBlur(TextAreaFocusOutEventArgs args)
    {
        Console.WriteLine("TextArea blurred");
        Console.WriteLine($"Current value: {args.Value}");
    }
}
```

#### Complete TextArea Events Example

```razor
<SfTextArea ID="Events" Placeholder="Enter your comments" FloatLabelType="FloatLabelType.Auto" Width="300px"
            OnBlur="@BlurHandler" ValueChange="@ChangeHandler" Created="@CreatedHandler"
            OnInput="@InputHandler" OnFocus="@FocusHandler">
</SfTextArea>
<div class="mt-3">
    <p><b>Event:</b> "@_eventMessage"</p>
</div>
@code {
    private string _eventMessage = string.Empty;

    private void CreatedHandler(object args)
    {
        _eventMessage = "Created event triggered";
    }

    private void InputHandler(TextAreaInputEventArgs args)
    {
        _eventMessage = "Input event triggered";
    }

    private void ChangeHandler(TextAreaValueChangeEventArgs args)
    {
        _eventMessage = "Changed event triggered";
    }

    private void BlurHandler(TextAreaFocusOutEventArgs args)
    {
        _eventMessage = "FocusOut event triggered";
    }

    private void FocusHandler(TextAreaFocusInEventArgs args)
    {
        _eventMessage = "FocusIn event triggered";
    }
}
```

## Practical Examples

### Login Form

```razor
<div class="login-form">
    <h2>Login</h2>
    
    <div class="form-group">
        <SfTextBox 
            @bind-Value="@username"
            Type="InputType.Text"
            Placeholder="Username"
            FloatLabelType="FloatLabelType.Auto"
            OnFocus="@(() => Console.WriteLine("Username focused"))">
        </SfTextBox>
    </div>

    <div class="form-group">
        <SfTextBox 
            @bind-Value="@password"
            Type="InputType.Password"
            Placeholder="Password"
            FloatLabelType="FloatLabelType.Auto">
        </SfTextBox>
    </div>

    <button @onclick="HandleLogin">Login</button>
</div>

@code {
    private string username = "";
    private string password = "";

    private void HandleLogin()
    {
        Console.WriteLine($"Login: {username}");
    }
}
```

### Comment Form

```razor
<div class="comment-form">
    <h3>Leave a Comment</h3>
    
    <div class="form-group">
        <label>Name:</label>
        <SfTextBox 
            @bind-Value="@commentData.Name"
            Placeholder="Your name"
            InputAttributes="@NameAttrs">
        </SfTextBox>
    </div>

    <div class="form-group">
        <label>Email:</label>
        <SfTextBox 
            @bind-Value="@commentData.Email"
            Type="InputType.Email"
            Placeholder="your@email.com"
            InputAttributes="@EmailAttrs">
        </SfTextBox>
    </div>

    <div class="form-group">
        <label>Comment:</label>
        <SfTextArea 
            @bind-Value="@commentData.Comment"
            Placeholder="Share your thoughts..."
            RowCount="5"
            MaxLength="500">
        </SfTextArea>
        <small>@commentData.Comment.Length / 500 characters</small>
    </div>

    <button @onclick="SubmitComment">Post Comment</button>
</div>

@code {
    private CommentModel commentData = new();
    private Dictionary<string, object> NameAttrs => new() { { "maxlength", "100" } };
    private Dictionary<string, object> EmailAttrs => new() { { "maxlength", "100" } };

    private void SubmitComment()
    {
        Console.WriteLine($"Comment from {commentData.Name}: {commentData.Comment}");
    }

    public class CommentModel
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Comment { get; set; } = "";
    }
}
```

### Search Bar

```razor
<div class="search-container">
    <SfTextBox 
        @bind-Value="@searchQuery"
        OnInput="@OnSearchInput"
        ShowClearButton="true"
        Placeholder="Search products..."
        CssClass="search-box"
        Width="300px">
    </SfTextBox>
    
    @if (!string.IsNullOrEmpty(searchQuery))
    {
        <div class="search-results">
            <p>Searching for: @searchQuery</p>
        </div>
    }
</div>

@code {
    private string searchQuery = "";

    private async Task OnSearchInput(InputEventArgs args)
    {
        searchQuery = args.Value ?? "";
        // Perform search operation
        await PerformSearch(searchQuery);
    }

    private async Task PerformSearch(string query)
    {
        // Search logic here
        Console.WriteLine($"Searching for: {query}");
    }
}
```
