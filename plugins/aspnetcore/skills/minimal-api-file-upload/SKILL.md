---
name: minimal-api-file-upload
description: >
  Implement file upload endpoints in ASP.NET Core minimal APIs (.NET 8+). USE FOR: handling IFormFile
  and IFormFileCollection parameters, configuring dual size limits (Kestrel + FormOptions), disabling
  anti-forgery on upload endpoints, validating file content via magic bytes, safe filename generation,
  streaming large files with MultipartReader. DO NOT USE FOR: MVC controller file uploads ([FromForm]
  works directly with attributes), simple JSON body endpoints, very large files over 1GB (use streaming
  with MultipartReader instead of IFormFile).
---

# Implementing File Uploads in ASP.NET Core Minimal APIs

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| File parameter(s) | Yes | IFormFile or IFormFileCollection |
| Size limits | Yes | Max file/request size |
| Allowed types | No | Content type or extension restrictions |

## Workflow

### Step 1: IFormFile Binding in Minimal APIs

```csharp
// In .NET 8+, IFormFile IS bound automatically from multipart form data
app.MapPost("/upload", (IFormFile file) => Results.Ok(file.FileName));

// BUT when you mix IFormFile with other parameters, annotate with [FromForm]
app.MapPost("/upload-with-metadata",
    ([FromForm] IFormFile file, [FromForm] string description) =>
{
    return Results.Ok(new { file.FileName, Description = description });
});

// For multiple files, use IFormFileCollection
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
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB — match your per-endpoint limit
});

// 2. Form options — multipart body length limit — default is 128MB
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 MB
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
// ⚠️ WARNING: Only disable anti-forgery for endpoints NOT authenticated with cookies.
// For cookie-authenticated endpoints, disabling anti-forgery opens CSRF attacks.
// JWT bearer auth and unauthenticated endpoints are safe to disable.
app.MapPost("/api/upload", (IFormFile file) => Results.Ok(file.FileName))
    .DisableAntiforgery();  // Safe for JWT/unauthenticated; UNSAFE for cookie auth

// COMMON MISTAKE: Getting 400 errors on file uploads and not realizing
// it's because UseAntiforgery() is in the pipeline
```

### Step 4: CRITICAL — Validate File Content, Not Just Extension

```csharp
app.MapPost("/upload", async (IFormFile file) =>
{
    // CRITICAL: Check content type AND file signature (magic bytes)
    // NEVER trust file extension alone — it can be spoofed

    var allowedTypes = new[] { "image/jpeg", "image/png" };
    if (!allowedTypes.Contains(file.ContentType))
        return Results.BadRequest("File type not allowed");

    // CRITICAL: Check magic bytes for file type verification
    // ContentType alone is client-provided and can be spoofed
    using var stream = file.OpenReadStream();
    var header = new byte[8];
    var bytesRead = await stream.ReadAsync(header, 0, 8);
    stream.Position = 0;

    if (bytesRead < 4)
        return Results.BadRequest("File too small to verify");

    // JPEG: FF D8 FF
    // PNG: 89 50 4E 47
    var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
    var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

    if (!isJpeg && !isPng)
        return Results.BadRequest("File content doesn't match declared type");

    // CRITICAL: Generate a safe filename — NEVER use user-provided filename directly
    // Derive extension from validated magic bytes, not from user-controlled file.FileName
    var extension = isJpeg ? ".jpg" : ".png";
    var safeFileName = $"{Guid.NewGuid()}{extension}";

    var filePath = Path.Combine("uploads", safeFileName);
    Directory.CreateDirectory("uploads");
    using var fileStream = File.Create(filePath);
    await file.CopyToAsync(fileStream);

    return Results.Ok(new { FileName = safeFileName, file.Length });
});
```

### Step 5: Streaming Large Files Without Buffering

```csharp
// NOTE: IFormFile buffers uploads to a temp file (not purely in-memory),
// but for very large files, use MultipartReader for true streaming
// to avoid excessive disk/memory usage during parsing.

app.MapPost("/upload-stream",
    [DisableRequestSizeLimit]
    async (HttpContext context) =>
{
    // Parse the multipart boundary from Content-Type header
    var contentType = context.Request.ContentType;
    if (contentType == null || !contentType.Contains("multipart/"))
        return Results.BadRequest("Not a multipart request");

    var boundary = HeaderUtilities.RemoveQuotes(
        MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
    if (string.IsNullOrEmpty(boundary))
        return Results.BadRequest("Missing boundary");

    var reader = new MultipartReader(boundary, context.Request.Body);

    while (await reader.ReadNextSectionAsync() is { } section)
    {
        // Check if this section has a Content-Disposition with a filename
        if (!ContentDispositionHeaderValue.TryParse(
                section.ContentDisposition, out var disposition) ||
            !disposition.IsFileDisposition())
            continue;

        var fileName = disposition.FileName.Value;
        var safeFile = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";

        // Stream directly to disk — never buffer in memory
        Directory.CreateDirectory("uploads");
        using var fileStream = File.Create(Path.Combine("uploads", safeFile));
        await section.Body.CopyToAsync(fileStream);
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
5. **Using IFormFile for very large files**: IFormFile buffers to a temp file during parsing. Use `MultipartReader` for true streaming.
6. **Using non-existent helper methods**: `GetMultipartBoundary()` and `GetContentDispositionHeader()` are NOT built-in ASP.NET Core APIs. Parse boundary from `MediaTypeHeaderValue` and use `ContentDispositionHeaderValue.TryParse()` directly.
