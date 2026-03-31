# OpenAPI Transformers and Filters

Transformer patterns for `Microsoft.AspNetCore.OpenApi` (.NET 9+) and equivalent
filter patterns for `Swashbuckle.AspNetCore`.

---

## Microsoft.AspNetCore.OpenApi Transformers

The built-in package exposes three transformer interfaces, all registered through
`AddOpenApi(options => ...)`:

| Interface | Scope | Use for |
|-----------|-------|---------|
| `IOpenApiDocumentTransformer` | Whole document | Title/version/contact metadata, security schemes, global tags |
| `IOpenApiOperationTransformer` | Single operation | Per-endpoint auth, deprecation, response headers |
| `IOpenApiSchemaTransformer` | Single schema | Enum string names, nullable annotations, polymorphism |

### Registering Transformers

```csharp
builder.Services.AddOpenApi(options =>
{
    // DI-enabled class transformer (can take constructor dependencies)
    options.AddDocumentTransformer<MyDocumentTransformer>();
    options.AddOperationTransformer<MyOperationTransformer>();
    options.AddSchemaTransformer<MySchemaTransformer>();

    // Inline lambda for simple one-liners
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info.Title = "Contoso API";
        document.Info.Version = "v1";
        document.Info.Contact = new OpenApiContact
        {
            Name = "Contoso Platform Team",
            Email = "api-support@contoso.com"
        };
        return Task.CompletedTask;
    });
});
```

DI-enabled transformers are registered as transient by default. If a transformer
is expensive to construct, register it explicitly:

```csharp
builder.Services.AddSingleton<MyDocumentTransformer>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<MyDocumentTransformer>();
});
```

---

### Document Transformer: XML Documentation

The built-in package reads XML summary comments through the `ApiDescription`
infrastructure when `GenerateDocumentationFile` is enabled. Set this in the `.csproj`:

```xml
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <!-- Suppress warnings for undocumented public members if needed -->
    <NoWarn>$(NoWarn);1591</NoWarn>
</PropertyGroup>
```

For **minimal APIs**, XML comments on the handler delegate are not picked up
automatically — use `.WithSummary()` and `.WithDescription()` on the endpoint
instead (shown in SKILL.md Step 5). For **MVC controller actions**, XML `<summary>`
comments are picked up automatically once the XML file exists on disk.

---

### Document Transformer: Multiple Documents with Different Metadata

When versioning produces multiple documents, the document name is available on the context:

```csharp
internal sealed class VersionedInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = context.DocumentName switch
        {
            "v1" => new OpenApiInfo { Title = "Contoso API", Version = "1.0" },
            "v2" => new OpenApiInfo { Title = "Contoso API", Version = "2.0",
                        Description = "Adds bulk operations and webhook support." },
            _ => document.Info
        };
        return Task.CompletedTask;
    }
}
```

---

### Operation Transformer: Per-Endpoint Security

Apply the Bearer requirement only to endpoints marked with `[Authorize]` or
`.RequireAuthorization()`. Endpoints marked `[AllowAnonymous]` or
`.AllowAnonymous()` are skipped.

```csharp
using Microsoft.AspNetCore.Authorization;

internal sealed class BearerOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        bool requiresAuth = metadata.OfType<IAuthorizeData>().Any();
        bool allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuth || allowsAnonymous)
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = []
        });

        return Task.CompletedTask;
    }
}
```

Register alongside the document transformer that declares the scheme:

```csharp
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();  // declares scheme
    options.AddOperationTransformer<BearerOperationTransformer>();      // applies per-endpoint
});
```

---

### Operation Transformer: Mark Deprecated Endpoints

```csharp
internal sealed class DeprecationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var isDeprecated = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ObsoleteAttribute>()
            .Any();

        if (isDeprecated)
            operation.Deprecated = true;

        return Task.CompletedTask;
    }
}
```

Mark endpoints in minimal APIs with a custom attribute or use the `[Obsolete]`
attribute on MVC controller actions.

---

### Schema Transformer: Enums as Strings

By default, enums serialize as integers in the spec. To represent them as string values:

```csharp
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type.IsEnum)
        {
            schema.Type = "string";
            schema.Enum = Enum.GetNames(context.JsonTypeInfo.Type)
                .Select(name => new OpenApiString(name))
                .Cast<IOpenApiAny>()
                .ToList();
        }

        return Task.CompletedTask;
    }
}
```

> This transformer only affects the OpenAPI schema. Ensure your `JsonSerializerOptions`
> (or `System.Text.Json` attributes) are also configured to serialize enums as strings at
> runtime — the spec and the actual wire format must agree.

---

### Document Transformer: OAuth2 Security Scheme

For OAuth2 authorization code flow (e.g., Azure AD, Entra ID):

```csharp
internal sealed class OAuth2SecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private readonly IConfiguration _configuration;

    public OAuth2SecuritySchemeTransformer(IConfiguration configuration)
        => _configuration = configuration;

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = _configuration["AzureAd:TenantId"]
            ?? throw new InvalidOperationException("AzureAd:TenantId not configured");
        var clientId = _configuration["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("AzureAd:ClientId not configured");

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["OAuth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri(
                        $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"),
                    TokenUrl = new Uri(
                        $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        [$"api://{clientId}/access_as_user"] = "Access the API as the signed-in user"
                    }
                }
            }
        };

        return Task.CompletedTask;
    }
}
```

Wire up Scalar to pre-populate the OAuth2 client ID:

```csharp
app.MapScalarApiReference(options =>
{
    options.WithOAuth2Authentication(oauth =>
    {
        oauth.ClientId = builder.Configuration["AzureAd:ClientId"]!;
    });
});
```

---

### Document Transformer: API Key Security Scheme

```csharp
document.Components ??= new OpenApiComponents();
document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
{
    Type = SecuritySchemeType.ApiKey,
    In = ParameterLocation.Header,
    Name = "X-Api-Key",
    Description = "API key passed in the X-Api-Key header"
};
```

---

## Swashbuckle Filter Equivalents

### IDocumentFilter → IOpenApiDocumentTransformer

```csharp
// Swashbuckle
public class TitleDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Info.Title = "Contoso API";
    }
}

// Register
builder.Services.AddSwaggerGen(options =>
{
    options.DocumentFilter<TitleDocumentFilter>();
});
```

### IOperationFilter → IOpenApiOperationTransformer

```csharp
// Swashbuckle
public class BearerOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresAuth = context.MethodInfo
            .GetCustomAttributes(true)
            .OfType<AuthorizeAttribute>()
            .Any()
            || context.MethodInfo.DeclaringType?
                .GetCustomAttributes(true)
                .OfType<AuthorizeAttribute>()
                .Any() == true;

        if (!requiresAuth)
            return;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = []
        });
    }
}

// Register
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<BearerOperationFilter>();
});
```

### ISchemaFilter → IOpenApiSchemaTransformer

```csharp
// Swashbuckle — enums as strings
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
            return;

        schema.Enum.Clear();
        foreach (var name in Enum.GetNames(context.Type))
            schema.Enum.Add(new OpenApiString(name));
    }
}
```

### JWT Security with Swashbuckle

```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        In = ParameterLocation.Header,
        BearerFormat = "JWT",
        Description = "Enter a valid JWT. Example: eyJhbGci..."
    });

    // Apply globally — or use an IOperationFilter for per-endpoint control.
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = []
    });
});
```
