# Tool Authoring Examples

Complete code examples for implementing tools (functions) in .NET agent applications.

## Basic Tool Definition

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

public class WeatherTools
{
    private readonly ILogger<WeatherTools> _logger;

    public WeatherTools(ILogger<WeatherTools> logger)
    {
        _logger = logger;
    }

    [Description("Gets the current weather for a city")]
    public async Task<string> GetWeatherAsync(
        [Description("The city name")] string city,
        [Description("Temperature unit: celsius or fahrenheit")] string unit = "celsius")
    {
        _logger.LogInformation("Getting weather for {City} in {Unit}", city, unit);
        
        // Simulate API call
        await Task.Delay(100);
        
        return unit == "celsius" 
            ? $"The weather in {city} is 22°C and sunny"
            : $"The weather in {city} is 72°F and sunny";
    }

    [Description("Gets the weather forecast for the next N days")]
    public async Task<string[]> GetForecastAsync(
        [Description("The city name")] string city,
        [Description("Number of days (1-7)")] int days = 5)
    {
        _logger.LogInformation("Getting {Days}-day forecast for {City}", days, city);
        
        if (days < 1 || days > 7)
        {
            throw new ArgumentException("Days must be between 1 and 7", nameof(days));
        }
        
        await Task.Delay(100);
        
        return Enumerable.Range(1, days)
            .Select(d => $"Day {d}: Partly cloudy, 20-25°C")
            .ToArray();
    }
}
```

## Registering Tools with IChatClient

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddSingleton<WeatherTools>();

builder.Services.AddSingleton<IChatClient>(services =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var weatherTools = services.GetRequiredService<WeatherTools>();
    
    // Create base chat client
    var baseClient = new AzureAIInferenceChatClient(
        new Uri(config["AzureOpenAI:Endpoint"]!),
        new ApiKeyCredential(config["AzureOpenAI:ApiKey"]!),
        config["AzureOpenAI:Deployment"]!);
    
    // Wrap with function invocation and tools
    return baseClient.AsBuilder()
        .UseFunctionInvocation()
        .UseTools(weatherTools)
        .Build();
});
```

## Tools with External API Calls

```csharp
public class WeatherApiTools
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherApiTools> _logger;
    private readonly string _apiKey;

    public WeatherApiTools(
        HttpClient httpClient, 
        IConfiguration config,
        ILogger<WeatherApiTools> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = config["WeatherAPI:ApiKey"] 
            ?? throw new InvalidOperationException("Weather API key not configured");
    }

    [Description("Gets the current weather for a city using a real weather API")]
    public async Task<string> GetCurrentWeatherAsync(
        [Description("The city name")] string city)
    {
        try
        {
            var url = $"https://api.weatherapi.com/v1/current.json?key={_apiKey}&q={city}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var data = await response.Content.ReadFromJsonAsync<WeatherApiResponse>();
            
            return $"Weather in {data.Location.Name}: {data.Current.TempC}°C, {data.Current.Condition.Text}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch weather for {City}", city);
            return $"Unable to fetch weather for {city}. Please try again later.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching weather for {City}", city);
            return "An unexpected error occurred. Please try again.";
        }
    }

    private record WeatherApiResponse(Location Location, Current Current);
    private record Location(string Name, string Country);
    private record Current(double TempC, double TempF, Condition Condition);
    private record Condition(string Text);
}

// Register with HttpClient factory
builder.Services.AddHttpClient<WeatherApiTools>();
```

## Tools with Database Access

```csharp
using Microsoft.EntityFrameworkCore;

public class ProductTools
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProductTools> _logger;

    public ProductTools(ApplicationDbContext context, ILogger<ProductTools> logger)
    {
        _context = context;
        _logger = logger;
    }

    [Description("Search for products by name or category")]
    public async Task<string> SearchProductsAsync(
        [Description("Search query")] string query,
        [Description("Maximum number of results")] int maxResults = 10)
    {
        try
        {
            var products = await _context.Products
                .Where(p => p.Name.Contains(query) || p.Category.Contains(query))
                .Take(maxResults)
                .Select(p => new { p.Id, p.Name, p.Price, p.Category })
                .ToListAsync();

            if (!products.Any())
            {
                return $"No products found matching '{query}'";
            }

            var result = string.Join("\n", products.Select(p => 
                $"- {p.Name} (ID: {p.Id}): ${p.Price} - {p.Category}"));
            
            return $"Found {products.Count} products:\n{result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching products for query: {Query}", query);
            return "An error occurred while searching for products.";
        }
    }

    [Description("Get detailed information about a specific product")]
    public async Task<string> GetProductDetailsAsync(
        [Description("Product ID")] int productId)
    {
        try
        {
            var product = await _context.Products
                .Include(p => p.Reviews)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
            {
                return $"Product with ID {productId} not found.";
            }

            var avgRating = product.Reviews.Any() 
                ? product.Reviews.Average(r => r.Rating) 
                : 0;

            return $@"Product: {product.Name}
Category: {product.Category}
Price: ${product.Price}
Description: {product.Description}
Average Rating: {avgRating:F1}/5 ({product.Reviews.Count} reviews)
Stock: {product.StockQuantity} units";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product details for ID: {ProductId}", productId);
            return "An error occurred while fetching product details.";
        }
    }
}
```

## Tools with Complex Return Types

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public class AnalyticsTools
{
    [Description("Get sales analytics for a date range")]
    public async Task<string> GetSalesAnalyticsAsync(
        [Description("Start date (yyyy-MM-dd)")] string startDate,
        [Description("End date (yyyy-MM-dd)")] string endDate)
    {
        // Parse dates
        if (!DateTime.TryParse(startDate, out var start) || 
            !DateTime.TryParse(endDate, out var end))
        {
            return "Invalid date format. Please use yyyy-MM-dd format.";
        }

        // Calculate analytics
        var analytics = new SalesAnalytics
        {
            StartDate = start,
            EndDate = end,
            TotalSales = 150000.50m,
            OrderCount = 425,
            AverageOrderValue = 352.94m,
            TopProducts = new[]
            {
                new ProductSales("Laptop Pro", 45000.00m, 30),
                new ProductSales("Wireless Mouse", 12500.00m, 250),
                new ProductSales("USB-C Cable", 3750.00m, 150)
            }
        };

        // Return as formatted JSON for the agent to parse
        return JsonSerializer.Serialize(analytics, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private record SalesAnalytics(
        DateTime StartDate,
        DateTime EndDate,
        decimal TotalSales,
        int OrderCount,
        decimal AverageOrderValue,
        ProductSales[] TopProducts);

    private record ProductSales(string Name, decimal Revenue, int UnitsSold);
}
```

## Tools with Validation

```csharp
using System.ComponentModel.DataAnnotations;

public class UserManagementTools
{
    private readonly ILogger<UserManagementTools> _logger;

    public UserManagementTools(ILogger<UserManagementTools> logger)
    {
        _logger = logger;
    }

    [Description("Create a new user account")]
    public async Task<string> CreateUserAsync(
        [Description("User's email address")] string email,
        [Description("User's full name")] string fullName,
        [Description("User's role: admin, user, or guest")] string role = "user")
    {
        // Validate email
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return "Invalid email address format.";
        }

        // Validate role
        var validRoles = new[] { "admin", "user", "guest" };
        if (!validRoles.Contains(role.ToLower()))
        {
            return $"Invalid role. Must be one of: {string.Join(", ", validRoles)}";
        }

        // Validate name
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length < 2)
        {
            return "Full name must be at least 2 characters long.";
        }

        try
        {
            // Create user logic here
            await Task.Delay(100); // Simulate API call
            
            _logger.LogInformation("Created user {Email} with role {Role}", email, role);
            return $"Successfully created user account for {fullName} ({email}) with role '{role}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create user {Email}", email);
            return "Failed to create user account. Please try again.";
        }
    }
}
```

## Best Practices for Tool Authoring

### 1. Always Use Async Methods

```csharp
// ✅ Good - async method
[Description("Get data")]
public async Task<string> GetDataAsync()
{
    return await _service.GetDataAsync();
}

// ❌ Bad - synchronous method (can block)
[Description("Get data")]
public string GetData()
{
    return _service.GetData();
}
```

### 2. Add Detailed Descriptions

```csharp
// ✅ Good - clear, specific description
[Description("Gets the current weather conditions including temperature, humidity, and conditions for a specific city. Returns temperature in the specified unit (celsius or fahrenheit).")]
public async Task<string> GetWeatherAsync(
    [Description("The name of the city to get weather for (e.g., 'Seattle', 'New York')")] string city,
    [Description("Temperature unit: 'celsius' for Celsius or 'fahrenheit' for Fahrenheit")] string unit = "celsius")
{
    // Implementation
}

// ❌ Bad - vague description
[Description("Gets weather")]
public async Task<string> GetWeatherAsync(string city, string unit = "celsius")
{
    // Implementation
}
```

### 3. Handle Errors Gracefully

```csharp
// ✅ Good - comprehensive error handling
[Description("Get user information")]
public async Task<string> GetUserAsync(int userId)
{
    try
    {
        var user = await _userService.GetUserAsync(userId);
        if (user == null)
        {
            return $"User with ID {userId} not found.";
        }
        return $"User: {user.Name}, Email: {user.Email}";
    }
    catch (UnauthorizedException)
    {
        return "You don't have permission to view this user.";
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "Network error fetching user {UserId}", userId);
        return "Unable to fetch user information due to a network error.";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error fetching user {UserId}", userId);
        return "An unexpected error occurred.";
    }
}

// ❌ Bad - no error handling
[Description("Get user information")]
public async Task<string> GetUserAsync(int userId)
{
    var user = await _userService.GetUserAsync(userId);
    return $"User: {user.Name}, Email: {user.Email}";
}
```

### 4. Use Default Parameter Values

```csharp
// ✅ Good - sensible defaults
[Description("Search products")]
public async Task<string> SearchProductsAsync(
    [Description("Search query")] string query,
    [Description("Maximum results to return")] int maxResults = 10,
    [Description("Sort by: 'name', 'price', or 'rating'")] string sortBy = "name")
{
    // Implementation
}
```

### 5. Return User-Friendly Messages

```csharp
// ✅ Good - clear, actionable message
return "Successfully created user account for John Doe (john@example.com). " +
       "A verification email has been sent.";

// ❌ Bad - technical or cryptic message
return "User inserted into database with ID 12345";
```
