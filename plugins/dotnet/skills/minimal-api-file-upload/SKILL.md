---
name: minimal-api-file-upload
description: File upload endpoints in ASP.NET minimal APIs (.NET 8+)
---

# Implementing File Uploads in ASP.NET Core Minimal APIs

## When to Use
- File upload endpoints in ASP.NET Core minimal APIs (.NET 8+)
- Handling IFormFile or IFormFileCollection parameters
- When you need size limits, content type validation, or streaming large files

## When Not to Use
- MVC controllers → `[FromForm] IFormFile` works directly with attributes
- Simple JSON body → no file upload needed
- Very large files (> 1GB) → use streaming with `MultipartReader` instead

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| File parameter(s) | Yes | IFormFile or IFormFileCollection |
| Size limits | Yes | Max file/request size |
| Allowed types | No | Content type or extension restrictions |

## Workflow

### Step 1: CRITICAL — IFormFile Requires [FromForm] in Minimal APIs (Not Automatic)

```csharp
// COMMON MISTAKE: Expecting IFormFile to bind automatically
app.MapPost("/upload", (IFormFile file) => ...);
// In early .NET versions, this worked differently. In .NET 8:

// CRITICAL: IFormFile IS bound automatically from form data in .NET 8
// BUT when you mix IFormFile with other parameters, you need [FromForm]
app.MapPost("/upload-with-metadata",
    ([FromForm] IFormFile file, [FromForm] string description) =>
{
    return Results.Ok(new { file.FileName, Description = description });
});

// CRITICAL: For multiple files, use IFormFileCollection
app.MapPost("/upload-multiple", (IFormFileCollection files) =>
{
    return Results.Ok(files.Select(f => new { f.FileName, f.Length }));
});
```

### Step 2: CRITICAL — File Size Limits Are Separate from Request Size Limits

```csharp
// CRITICAL: There are TWO different size limits and you need to configure BOTH

// 1. Request body size limit (Kestrel level) — default is 30MB
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
});

// 2. Form options — multipart body length limit — default is 128MB
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100 MB
    options.ValueLengthLimit = 1024 * 1024; // 1 MB for form values
    options.MultipartHeadersLengthLimit = 16384; // 16 KB for section headers
});

// COMMON MISTAKE: Only increasing Kestrel MaxRequestBodySize
// upload still fails because FormOptions.MultipartBodyLengthLimit is exceeded

// COMMON MISTAKE: Only increasing FormOptions
// upload fails with "Request body too large" from Kestrel before reaching form parsing

// CRITICAL: Per-endpoint override with RequestSizeLimit attribute
app.MapPost("/upload-large", [RequestSizeLimit(200_000_000)] (IFormFile file) =>
{
    return Results.Ok(new { file.FileName, file.Length });
});

// CRITICAL: To disable the limit entirely (for streaming):
app.MapPost("/upload-unlimited", [DisableRequestSizeLimit] async (HttpContext context) =>
{
    // Handle manually
});
```

### Step 3: CRITICAL — Anti-Forgery Auto-Validates Form Uploads in .NET 8

```csharp
// CRITICAL: In .NET 8 with UseAntiforgery(), ALL form-bound endpoints
// automatically validate anti-forgery tokens, INCLUDING file uploads

builder.Services.AddAntiforgery();
var app = builder.Build();
app.UseAntiforgery();

// This endpoint now REQUIRES an anti-forgery token:
app.MapPost("/upload", (IFormFile file) => Results.Ok(file.FileName));
// Without the token → 400 Bad Request

// CRITICAL: For API-only file uploads (no anti-forgery needed), opt out:
app.MapPost("/api/upload", (IFormFile file) => Results.Ok(file.FileName))
    .DisableAntiforgery();  // CRITICAL: Must explicitly opt out

// COMMON MISTAKE: Getting 400 errors on file uploads and not realizing
// it's because UseAntiforgery() is in the pipeline
```

### Step 4: CRITICAL — Validate File Content, Not Just Extension

```csharp
app.MapPost("/upload", async (IFormFile file) =>
{
    // CRITICAL: Check content type AND file signature (magic bytes)
    // NEVER trust file extension alone — it can be spoofed

    var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
    if (!allowedTypes.Contains(file.ContentType))
        return Results.BadRequest("File type not allowed");

    // CRITICAL: Check magic bytes for file type verification
    using var stream = file.OpenReadStream();
    var header = new byte[8];
    await stream.ReadAsync(header, 0, 8);
    stream.Position = 0;

    // JPEG: FF D8 FF
    // PNG: 89 50 4E 47
    var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

    if (!isJpeg && !isPng)
        return Results.BadRequest("File content doesn't match declared type");

    // CRITICAL: Generate a safe filename — never use user-provided filename directly
    var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
    // NEVER: var path = Path.Combine("uploads", file.FileName);  // Path traversal!

    var filePath = Path.Combine("uploads", safeFileName);
    Directory.CreateDirectory("uploads");
    using var fileStream = File.Create(filePath);
    await file.CopyToAsync(fileStream);

    return Results.Ok(new { FileName = safeFileName, file.Length });
});
```

### Step 5: CRITICAL — Streaming Large Files Without Buffering

```csharp
// CRITICAL: IFormFile buffers the entire file in memory by default
// For large files, use MultipartReader for streaming

app.MapPost("/upload-stream",
    [DisableRequestSizeLimit]
    async (HttpContext context) =>
{
    var boundary = context.Request.GetMultipartBoundary();
    if (string.IsNullOrEmpty(boundary))
        return Results.BadRequest("Not a multipart request");

    var reader = new MultipartReader(boundary, context.Request.Body);

    // CRITICAL: ReadNextSectionAsync returns null when there are no more sections
    while (await reader.ReadNextSectionAsync() is { } section)
    {
        var contentDisposition = section.GetContentDispositionHeader();
        if (contentDisposition == null) continue;

        if (contentDisposition.IsFileDisposition())
        {
            var fileName = contentDisposition.FileName.Value;
            var safeFile = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";

            // CRITICAL: Stream directly to disk — never buffer in memory
            using var fileStream = File.Create(Path.Combine("uploads", safeFile));
            await section.Body.CopyToAsync(fileStream);
        }
    }

    return Results.Ok("Uploaded");
}).DisableAntiforgery();

// COMMON MISTAKE: Using file.CopyToAsync for very large files
// IFormFile buffers everything in memory first — can cause OutOfMemoryException
```

## Common Mistakes

1. **Only configuring one size limit**: Must configure BOTH Kestrel `MaxRequestBodySize` AND `FormOptions.MultipartBodyLengthLimit`.
2. **400 errors from anti-forgery**: In .NET 8, `UseAntiforgery()` auto-validates form uploads. Use `.DisableAntiforgery()` for API endpoints.
3. **Trusting file.FileName**: User-provided filename can contain path traversal. Always generate a safe filename.
4. **Trusting Content-Type only**: Content type can be spoofed. Check magic bytes for actual file type.
5. **Using IFormFile for large files**: IFormFile buffers in memory. Use `MultipartReader` for streaming.
6. **Missing GetMultipartBoundary extension**: Must use `context.Request.GetMultipartBoundary()`, not parse manually.
