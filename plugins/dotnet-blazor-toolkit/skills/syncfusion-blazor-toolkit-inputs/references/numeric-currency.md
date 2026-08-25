# NumericTextBox Component - Numeric and Currency Formatting

## Table of Contents
- [SfNumericTextBox Overview](#sfnumerictextbox-overview)
- [Generic Type Implementation](#generic-type-implementation)
- [Basic Properties](#basic-properties)
- [Currency Formatting](#currency-formatting)
- [Percentage Formatting](#percentage-formatting)
- [Min/Max Constraints](#minmax-constraints)
- [Step and Spinner](#step-and-spinner)
- [Decimal Places](#decimal-places)
- [Events and Validation](#events-and-validation)
- [Practical Examples](#practical-examples)

## SfNumericTextBox Overview

**SfNumericTextBox<T>** is a typed numeric input component for entering and formatting numbers. It supports currency, percentage, decimals, and custom formatting with built-in validation and spinner controls.

**When to use SfNumericTextBox:**
- Currency amounts and prices
- Percentages and rates
- Measurements and quantities
- Age, ratings, or count inputs
- Any numeric data with formatting

### Basic Implementation

```razor
<SfNumericTextBox TValue="int" Value="42"></SfNumericTextBox>
```

### With Value Binding

```razor
<SfNumericTextBox TValue="int" @bind-Value="@quantity"></SfNumericTextBox>

@code {
    private int quantity = 1;
}
```

## Generic Type Implementation

**SfNumericTextBox<T>** requires a generic type parameter. The `TValue` can be:

| Type | Use Case | Example |
|------|----------|---------|
| `int` | Whole numbers | Product quantities |
| `long` | Large whole numbers | Big amounts |
| `float` | Single-precision decimals | Measurements |
| `double` | Double-precision decimals | Scientific values |
| `decimal` | Currency precision | Money amounts |
| `int?` | Nullable integer | Optional counts |
| `decimal?` | Nullable decimal | Optional prices |

### Type-Specific Examples

```razor
@* Whole number *@
<SfNumericTextBox TValue="int" Value="100"></SfNumericTextBox>

@* Decimal for currency *@
<SfNumericTextBox TValue="decimal" Value="99.99m"></SfNumericTextBox>

@* Nullable for optional input *@
<SfNumericTextBox TValue="int?" Value="null"></SfNumericTextBox>

@* Double for precision *@
<SfNumericTextBox TValue="double" Value="3.14159"></SfNumericTextBox>
```

## Basic Properties

### Essential Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Value` | `T` | `default(T)` | Current numeric value |
| `Min` | `T` | Varies by type | Minimum allowed value |
| `Max` | `T` | Varies by type | Maximum allowed value |
| `Step` | `T` | Varies by type | Increment/decrement step |
| `Disabled` | `bool` | `false` | Disable input |
| `Readonly` | `bool` | `false` | Prevent modification |
| `Placeholder` | `string` | `null` | Placeholder text |
| `Format` | `string` | `"n2"` | Number format string |

### Display Properties

| Property | Type | Description |
|----------|------|-------------|
| `ShowSpinButton` | `bool` | Show +/- spinner buttons |
| `ShowClearButton` | `bool` | Show clear button |
| `Decimals` | `int?` | Number of decimal places to display |
| `FloatLabelType` | `FloatLabelType` | Floating label behavior |

### Currency and Validation Properties

| Property | Type | Description |
|----------|------|-------------|
| `Currency` | `string?` | ISO 4217 currency code (e.g., "USD", "EUR") |
| `ValidateDecimalOnType` | `bool` | Validate decimals during typing |
| `StrictMode` | `bool` | Restrict values between Min/Max (default: true) |
| `AllowMouseWheel` | `bool` | Enable mouse wheel increment/decrement |

## Currency Formatting

### Basic Currency Format

```razor
<div class="price-input">
    <label>Price (USD):</label>
    <SfNumericTextBox 
        TValue="decimal" 
        Currency="USD"
        Format="C2"
        Value="29.99m"
        Min="0"
        Max="10000">
    </SfNumericTextBox>
</div>
```

**Format String Reference:**
- `C` or `C2` - Currency with 2 decimals ($29.99)
- `C0` - Currency with no decimals ($30)
- `C3` - Currency with 3 decimals ($29.990)

### Currency with Value Binding

```razor
<div class="shopping-cart">
    <div>
        <label>Product Price:</label>
        <SfNumericTextBox 
            TValue="decimal" 
            @bind-Value="@productPrice"
            Currency="USD"
            Format="C2"
            Min="0"
            Placeholder="$0.00"
            FloatLabelType="FloatLabelType.Never">
        </SfNumericTextBox>
    </div>

    <div>
        <label>Quantity:</label>
        <SfNumericTextBox 
            TValue="int"
            @bind-Value="@quantity"
            Min="1"
            Max="999">
        </SfNumericTextBox>
    </div>

    <div>
        <strong>Total: @($"{(productPrice * quantity):C2}")</strong>
    </div>
</div>

@code {
    private decimal productPrice = 29.99m;
    private int quantity = 1;
}
```

### Multiple Currency Formats

```razor
<div class="currency-demo">
    <h3>Currency Formatting</h3>

    <div>
        <label>USD (C2):</label>
        <SfNumericTextBox TValue="decimal" Format="C2" Value="1234.56m"></SfNumericTextBox>
    </div>

    <div>
        <label>Percentage (P):</label>
        <SfNumericTextBox TValue="decimal" Format="P2" Value="0.15m"></SfNumericTextBox>
    </div>

    <div>
        <label>Number (N):</label>
        <SfNumericTextBox TValue="decimal" Format="N2" Value="1234.56m"></SfNumericTextBox>
    </div>

    <div>
        <label>Fixed decimals (F3):</label>
        <SfNumericTextBox TValue="decimal" Format="F3" Value="1234.5678m"></SfNumericTextBox>
    </div>
</div>
```

## Percentage Formatting

### Percentage Input

```razor
<div class="discount-input">
    <label>Discount Rate:</label>
    <SfNumericTextBox 
        TValue="decimal"
        Format="P"
        Value="0.15m"
        Min="0"
        Max="1"
        Placeholder="0%"
        FloatLabelType="FloatLabelType.Never">
    </SfNumericTextBox>
</div>
```

**Note:** Percentage format expects decimal values (0.15 = 15%)

### Tax Calculator with Percentages

```razor
<div class="tax-calculator">
    <h3>Tax Calculator</h3>

    <div>
        <label>Subtotal:</label>
        <SfNumericTextBox 
            TValue="decimal"
            @bind-Value="@subtotal"
            Format="C2"
            Min="0">
        </SfNumericTextBox>
    </div>

    <div>
        <label>Tax Rate (%):</label>
        <SfNumericTextBox 
            TValue="decimal"
            @bind-Value="@taxRate"
            Format="P"
            Min="0"
            Max="1"
            Step="0.01m">
        </SfNumericTextBox>
    </div>

    <div>
        <label>Tax Amount:</label>
        <SfNumericTextBox 
            TValue="decimal"
            Value="@(subtotal * taxRate)"
            Format="C2"
            Readonly="true">
        </SfNumericTextBox>
    </div>

    <div>
        <strong>Total: @($"{(subtotal + subtotal * taxRate):C2}")</strong>
    </div>
</div>

@code {
    private decimal subtotal = 100m;
    private decimal taxRate = 0.08m;
}
```

## Min/Max Constraints

### Setting Boundaries

```razor
<div class="age-input">
    <label>Age (18-120):</label>
    <SfNumericTextBox 
        TValue="int"
        @bind-Value="@age"
        Min="18"
        Max="120"
        Placeholder="Enter age"
        FloatLabelType="FloatLabelType.Auto">
    </SfNumericTextBox>
</div>

@code {
    private int age = 25;
}
```

### Dynamic Min/Max

```razor
<div class="date-input-demo">
    <label>Select Quantity Range:</label>
    
    <div>
        <label>Minimum:</label>
        <SfNumericTextBox 
            TValue="int"
            @bind-Value="@minValue"
            Min="0"
            Max="@maxValue">
        </SfNumericTextBox>
    </div>

    <div>
        <label>Maximum:</label>
        <SfNumericTextBox 
            TValue="int"
            @bind-Value="@maxValue"
            Min="@minValue"
            Max="1000">
        </SfNumericTextBox>
    </div>

    <p>Valid range: @minValue - @maxValue</p>
</div>

@code {
    private int minValue = 10;
    private int maxValue = 100;
}
```

## Step and Spinner

### Spinner Buttons

The spinner buttons allow increment/decrement by the step value:

```razor
<div class="quantity-selector">
    <label>Quantity:</label>
    <SfNumericTextBox 
        TValue="int"
        @bind-Value="@quantity"
        Min="1"
        Max="100"
        Step="1"
        ShowSpinButton="true">
    </SfNumericTextBox>
</div>

@code {
    private int quantity = 1;
}
```

### Custom Step Values

```razor
<div class="step-demo">
    <h3>Different Step Values</h3>

    <div>
        <label>By 1:</label>
        <SfNumericTextBox TValue="int" Step="1" Min="0" Max="100"></SfNumericTextBox>
    </div>

    <div>
        <label>By 5:</label>
        <SfNumericTextBox TValue="int" Step="5" Min="0" Max="100"></SfNumericTextBox>
    </div>

    <div>
        <label>By 0.01 (decimal):</label>
        <SfNumericTextBox TValue="decimal" Step="0.01m" Min="0" Max="1"></SfNumericTextBox>
    </div>

    <div>
        <label>By 10% (percentage):</label>
        <SfNumericTextBox TValue="decimal" Step="0.10m" Format="P" Min="0" Max="1"></SfNumericTextBox>
    </div>
</div>
```

## Decimal Places

### Decimals Property

Forces a fixed number of decimal places:

```razor
<div class="precision-demo">
    <h3>Decimal Places</h3>

    <div>
        <label>2 Decimal Places (Currency):</label>
        <SfNumericTextBox 
            TValue="decimal"
            Decimals="2"
            Format="C2"
            Value="29.99m">
        </SfNumericTextBox>
    </div>

    <div>
        <label>4 Decimal Places (Scientific):</label>
        <SfNumericTextBox 
            TValue="decimal"
            Decimals="4"
            Format="F4"
            Value="3.14159m">
        </SfNumericTextBox>
    </div>

    <div>
        <label>0 Decimal Places (Whole):</label>
        <SfNumericTextBox 
            TValue="int"
            Decimals="0"
            Value="42">
        </SfNumericTextBox>
    </div>
</div>
```

## Events and Validation

### ValueChange Event

```razor
<SfNumericTextBox 
    TValue="int"
    ValueChange="@OnValueChanged"
    @bind-Value="@myNumber">
</SfNumericTextBox>

@code {
    private int myNumber = 0;

    private void OnValueChanged(ChangeEventArgs<int> args)
    {
        Console.WriteLine($"Old value: {args.PreviousValue}");
        Console.WriteLine($"New value: {args.Value}");
    }
}
```

### OnInput Event

Real-time event fired on every value change during typing:

```razor
<SfNumericTextBox 
    TValue="int"
    OnInput="@OnInput"
    @bind-Value="@myNumber">
</SfNumericTextBox>

@code {
    private int myNumber = 0;

    private void OnInput(ChangeEventArgs args)
    {
        Console.WriteLine($"Input: {args.Value}");
    }
}
```

### Created Event

```razor
<SfNumericTextBox Created="@OnCreated" TValue="int"></SfNumericTextBox>

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("NumericTextBox created");
    }
}
```

### Destroyed Event

```razor
<SfNumericTextBox Destroyed="@OnDestroyed" TValue="int"></SfNumericTextBox>

@code {
    private void OnDestroyed(object args)
    {
        Console.WriteLine("NumericTextBox destroyed");
    }
}
```

### Focus and Blur Events

```razor
<SfNumericTextBox 
    TValue="decimal"
    OnFocus="@OnFocus"
    OnBlur="@OnBlur"
    @bind-Value="@price"
    Format="C2">
</SfNumericTextBox>
<p>@validationMessage</p>

@code {
    private decimal price = 0m;
    private string validationMessage = "";

    private void OnFocus(NumericFocusEventArgs<decimal> args)
    {
        validationMessage = "Enter a valid price";
    }

    private void OnBlur(NumericBlurEventArgs<decimal> args)
    {
        if (price <= 0)
        {
            validationMessage = "❌ Price must be greater than 0";
        }
        else
        {
            validationMessage = "✓ Valid price";
        }
    }
}
```

### Form Validation with DataAnnotations

```razor
@using System.ComponentModel.DataAnnotations

<EditForm Model="@product" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />

    <div class="form-group">
        <label>Product Name:</label>
        <SfTextBox @bind-Value="@product.Name" Placeholder="Name"></SfTextBox>
        <ValidationMessage For="@(() => product.Name)" />
    </div>

    <div class="form-group">
        <label>Price:</label>
        <SfNumericTextBox 
            TValue="decimal"
            @bind-Value="@product.Price"
            Format="C2"
            Min="0">
        </SfNumericTextBox>
        <ValidationMessage For="@(() => product.Price)" />
    </div>

    <button type="submit">Save Product</button>
</EditForm>

@code {
    private ProductModel product = new();

    private void HandleSubmit()
    {
        Console.WriteLine($"Product: {product.Name}, Price: {product.Price}");
    }

    public class ProductModel
    {
        [Required(ErrorMessage = "Product name is required")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, 999999.99, ErrorMessage = "Price must be between 0.01 and 999999.99")]
        public decimal Price { get; set; }
    }
}
```

## Public Methods

SfNumericTextBox provides the following public methods for programmatic control:

### IncrementAsync

Increments the value by the specified step amount:

```razor
<SfNumericTextBox @ref="numericRef" TValue="int" @bind-Value="@quantity" Min="0" Max="100" Step="1"></SfNumericTextBox>
<button @onclick="IncrementValue">+ Increment</button>

@code {
    private SfNumericTextBox<int> numericRef = new();
    private int quantity = 0;

    private async Task IncrementValue()
    {
        await numericRef.IncrementAsync(1);
    }
}
```

### DecrementAsync

Decrements the value by the specified step amount:

```razor
<SfNumericTextBox @ref="numericRef" TValue="int" @bind-Value="@quantity" Min="0" Max="100" Step="1"></SfNumericTextBox>
<button @onclick="DecrementValue">- Decrement</button>

@code {
    private SfNumericTextBox<int> numericRef = new();
    private int quantity = 0;

    private async Task DecrementValue()
    {
        await numericRef.DecrementAsync(1);
    }
}
```

### FocusAsync

Sets focus to the NumericTextBox for user interaction:

```razor
<SfNumericTextBox @ref="numericRef" TValue="decimal" @bind-Value="@price" Format="C2"></SfNumericTextBox>
<button @onclick="FocusNumeric">Focus Input</button>

@code {
    private SfNumericTextBox<decimal> numericRef = new();
    private decimal price = 0m;

    private async Task FocusNumeric()
    {
        await numericRef.FocusAsync();
    }
}
```

### FocusOutAsync

Removes focus from the NumericTextBox:

```razor
<SfNumericTextBox @ref="numericRef" TValue="decimal" @bind-Value="@price" Format="C2"></SfNumericTextBox>
<button @onclick="BlurNumeric">Blur Input</button>

@code {
    private SfNumericTextBox<decimal> numericRef = new();
    private decimal price = 0m;

    private async Task BlurNumeric()
    {
        await numericRef.FocusOutAsync();
    }
}
```

### GetFormattedText

Gets the formatted text representation of the current value:

```razor
<SfNumericTextBox @ref="numericRef" TValue="decimal" @bind-Value="@price" Format="C2" Min="0"></SfNumericTextBox>
<button @onclick="ShowFormatted">Show Formatted</button>
<p>Formatted: @formattedText</p>

@code {
    private SfNumericTextBox<decimal> numericRef = new();
    private decimal price = 29.99m;
    private string formattedText = "";

    private void ShowFormatted()
    {
        formattedText = numericRef.GetFormattedText() ?? "";
    }
}
```

## Practical Examples

### Shopping Cart Price Calculator

```razor
<div class="shopping-cart-calculator">
    <h2>Shopping Cart</h2>

    <table>
        <thead>
            <tr>
                <th>Item</th>
                <th>Unit Price</th>
                <th>Quantity</th>
                <th>Total</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var item in cartItems)
            {
                <tr>
                    <td>@item.Name</td>
                    <td>
                        <SfNumericTextBox 
                            TValue="decimal"
                            @bind-Value="@item.UnitPrice"
                            Format="C2"
                            ValueChange="@RecalculateTotal">
                        </SfNumericTextBox>
                    </td>
                    <td>
                        <SfNumericTextBox 
                            TValue="int"
                            @bind-Value="@item.Quantity"
                            Min="1"
                            Max="999"
                            ValueChange="@OnQuantityChanged">
                        </SfNumericTextBox>
                    </td>
                    <td>@((item.UnitPrice * item.Quantity).ToString("C2"))</td>
                </tr>
            }
        </tbody>
    </table>

    <div class="summary">
        <h3>Subtotal: @subtotal.ToString("C2")</h3>
        <h3>Tax (8%): @tax.ToString("C2")</h3>
        <h2>Total: @total.ToString("C2")</h2>
    </div>
</div>

@code {
    private List<CartItem> cartItems = new()
    {
        new CartItem { Name = "Item 1", UnitPrice = 29.99m, Quantity = 1 },
        new CartItem { Name = "Item 2", UnitPrice = 49.99m, Quantity = 2 },
    };

    private decimal subtotal = 0m;
    private decimal tax = 0m;
    private decimal total = 0m;

    private void RecalculateTotal(ChangeEventArgs<decimal> args)
    {
        subtotal = cartItems.Sum(x => x.UnitPrice * x.Quantity);
        tax = subtotal * 0.08m;
        total = subtotal + tax;
    }

    private void OnQuantityChanged(ChangeEventArgs<int> args)
    {
        subtotal = cartItems.Sum(x => x.UnitPrice * x.Quantity);
        tax = subtotal * 0.08m;
        total = subtotal + tax;
    }

    public class CartItem
    {
        public string Name { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }
}
```

### Rating with Score

```razor
<div class="rating-input">
    <h3>Rate this product (1-5 stars):</h3>
    
    <SfNumericTextBox 
        TValue="decimal"
        @bind-Value="@rating"
        Min="1"
        Max="5"
        Step="0.5m"
        Format="N1"
        ShowSpinButton="true">
    </SfNumericTextBox>

    <p>Your rating: 
        @for (int i = 0; i < (int)rating; i++)
        {
            <span>⭐</span>
        }
    </p>
</div>

@code {
    private decimal rating = 3m;
}
```
