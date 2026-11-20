# ASP.NET Core Breaking Changes (.NET 11)

These breaking changes affect ASP.NET Core projects. Sources: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/overview and https://learn.microsoft.com/en-us/aspnet/core/migration/100-to-110

## Source-Incompatible Changes

### Microsoft.OpenApi updated to v3 with OpenAPI 3.2.0 support (Preview 2)

**Impact: Medium.** `Microsoft.AspNetCore.OpenApi` updated its dependency from `Microsoft.OpenApi` 2.x to 3.x, adding OpenAPI 3.2.0 document generation. The underlying `Microsoft.OpenApi` library has breaking API changes in the v2→v3 transition.

Code that directly uses `Microsoft.OpenApi` types (`OpenApiDocument`, `OpenApiSchema`, `OpenApiOperation`, etc.) will have compile errors.

**Fix:** Follow the [Microsoft.OpenApi v3 upgrade guide](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-3.md). If you only use the ASP.NET Core OpenAPI integration (`.WithOpenApi()`, `MapOpenApi()`) without touching the object model directly, no changes are needed.

Source: https://github.com/dotnet/aspnetcore/pull/65415

### EditContext.Validate() is obsolete; use ValidateAsync() (Preview 7)

**Impact: High (for Blazor forms in projects using `TreatWarningsAsErrors`).** `EditContext.Validate()` is marked `[Obsolete("Validate is obsolete. Use ValidateAsync instead.")]`. There is no custom diagnostic ID, so the warning is CS0618. `Validate()` runs synchronous validators only, and throws `InvalidOperationException` if a validation handler registers asynchronous work. `EditForm` itself now calls `ValidateAsync()` before `OnValidSubmit` or `OnInvalidSubmit`.

```csharp
// .NET 10
if (editContext.Validate())
{
    await SaveAsync();
}

// .NET 11
if (await editContext.ValidateAsync())
{
    await SaveAsync();
}
```

**Fix:** Replace `Validate()` with `await ValidateAsync()` and make the calling method async. The change tends to spread upward through form components, dialogs, and multi-step forms, so plan for it. Watch for these patterns:

- **Null-conditional checks:** `editContext?.Validate() is false` cannot simply become `editContext?.ValidateAsync() is false`, because a `Task<bool>` is never `false`. Rewrite it as `editContext is not null && !await editContext.ValidateAsync()`.
- **Event handlers:** a synchronous `EditContext.OnFieldChanged` handler that calls `Validate()` would have to become `async void`. Validate the whole form from the submit path instead. Where per-field validation is needed while the user types, call `ValidateAsync()` from the input's own async event handler (for example `@onchange` or `@bind:after`), which the component awaits.
- **Custom validators:** subscribe to `OnValidationRequested` and register async work with `ValidationRequestedEventArgs.AddAsyncValidator(...)`. `ValidationRequestedEventArgs.IsAsync` is `false` when the caller used `Validate()`.

Sources: https://github.com/dotnet/aspnetcore/pull/67662, https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.forms.editcontext.validate?view=aspnetcore-11.0

### Obsolete Blazor APIs removed

**Impact: Medium.** Blazor APIs that were already obsolete have been removed:

| Removed API | Replacement |
|-------------|-------------|
| `Router.PreferExactMatches` | Remove the assignment (exact matching is the default since .NET 6) |
| `ResourceAsset(string url)` constructor | `ResourceAsset(string url, IReadOnlyList<ResourceAssetProperty>? properties)` |
| `EditContext.AddDataAnnotationsValidation()` and `EnableDataAnnotationsValidation()` without `IServiceProvider` | `EnableDataAnnotationsValidation(IServiceProvider)` |
| `RemoteBrowserFileStreamOptions` | `BrowserFileStreamOptions` |
| `WebEventCallbackFactoryEventArgsExtensions` | Use `EventCallbackFactory` directly |
| `WebRenderer.RendererId` `init` accessor | Pass the renderer ID to the `WebRenderer` constructor |
| `SignOutSessionStateManager` | Delete usages (no-op since .NET 7) |
| `RemoteAuthenticationService` constructor without a logger, `AccessTokenResult` constructor and `RedirectUrl` | Constructors that take a logger; `InteractiveRequestUrl` |

Learn lists this as source incompatible, but it is also a binary break for third-party Blazor component libraries compiled against an earlier ASP.NET Core version. A library that still calls a removed API (for example `WebEventCallbackFactoryEventArgsExtensions`) fails at run time with `TypeLoadException` or `MissingMethodException` when the component renders. Nothing shows at compile time.

**Fix:** Update your own code to the replacements. Before upgrading, check that each third-party Blazor component library has a version built for .NET 11, and run UI or render tests rather than relying on a clean build.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/blazor-obsolete-apis-removed

### MVC compatibility options removed (Preview 7)

**Impact: Low.** `SetCompatibilityVersion`, `CompatibilityVersion`, `MvcCompatibilityOptions`, and `ConfigureCompatibilityOptions<TOptions>` have been removed. They were no-ops since ASP.NET Core 3.0. Code fails to compile, and binaries compiled against them fail with `MissingMethodException` or `TypeLoadException`.

**Fix:** Delete the calls: `services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Latest)` becomes `services.AddMvc()`.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/mvc-compatibility-options-removed

### ConcurrencyLimiter middleware removed (Preview 1)

**Impact: Low.** The `Microsoft.AspNetCore.ConcurrencyLimiter` package (obsolete since ASP.NET Core 8, ASP0025) has no 11.x version. `UseConcurrencyLimiter`, `AddQueuePolicy`, and `AddStackPolicy` are gone, and restoring the package at version 11 fails.

**Fix:** Migrate to the rate limiting middleware (the 9.x and 10.x packages still restore and can be used while targeting `net11.0` as an interim step): `AddRateLimiter(o => o.AddConcurrencyLimiter("policy", ...))`, `app.UseRateLimiter()`, and `.RequireRateLimiting("policy")`.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/concurrencylimiter-removed

### New Blazor analyzers BL0011 to BL0016

**Impact: Medium (for projects using `TreatWarningsAsErrors`).** New Blazor analyzers are enabled as warnings by default. There are no Learn pages for them yet; the behavior below comes from the analyzer source in `src/Components/Analyzers` in dotnet/aspnetcore.

| ID | Flags |
|----|-------|
| BL0011 | `<Virtualize>` with `ItemsProvider` but no `ItemComparer` |
| BL0012 | Unnecessary `StateHasChanged()` call |
| BL0013 | `AuthenticationStateProvider` used without subscribing to `AuthenticationStateChanged` |
| BL0014 | A `for` loop variable captured in a closure, event handler, or `RenderFragment` in Razor markup |
| BL0015 | A non-public `[JSInvokable]` method |
| BL0016 | A JS interop call that is not inside a try/catch |

Notes for giving correct advice:

- **BL0012** inspects only `ComponentBase`-derived types, and only `OnInitialized(Async)`, `OnParametersSet(Async)`, and methods bound as event handlers through `EventCallbackFactory`. It flags calls in synchronous methods, and calls before the first `await` or after the last one. It does not consider other callers of the same method. A method that is bound to an event and is also called from `OnAfterRenderAsync`, through a component reference (`@ref`), from a timer, or from a `[JSInvokable]` callback may still need its explicit render on those paths. Check every caller before removing the call.
- **BL0016** treats any enclosing `try` with at least one `catch` clause, of any exception type, as a guard. The call must be in the `try` body, and the search stops at a lambda or local function boundary, so a `try` around the code that creates a lambda doesn't guard calls inside it. It flags calls on any type that implements `IJSRuntime`, `IJSInProcessRuntime`, `IJSObjectReference`, or `IJSInProcessObjectReference`, including test doubles. Calls through your own extension methods are not flagged at the call site, so a shared helper that catches `JSDisconnectedException` (and is itself guarded) satisfies it without per-call pragmas. Disabling BL0016 for test projects (`<NoWarn>$(NoWarn);BL0016</NoWarn>` when `IsTestProject` is `true`) is reasonable.
- **BL0014** usually points at a real bug: the lambda sees the final loop value. Copy the loop variable to a local inside the loop, or use `foreach`.

Sources: https://github.com/dotnet/aspnetcore/pull/67176, https://github.com/dotnet/aspnetcore/pull/67900, https://github.com/dotnet/aspnetcore/pull/67228

## Behavioral Changes

### Automatic cross-origin CSRF protection middleware

**Impact: Low–Medium.** For most apps no code change is required, but the request pipeline changes. Apps built with `WebApplication.CreateBuilder` now get a CSRF protection middleware by default. It inspects the `Sec-Fetch-Site` and `Origin` headers on unsafe requests (not `GET`, `HEAD`, `OPTIONS`, `TRACE`) and records a verdict for endpoints with antiforgery metadata that requires validation:

- Blazor static SSR endpoints
- Minimal API endpoints that bind form data
- MVC actions with `[ValidateAntiForgeryToken]` or `[AutoValidateAntiforgeryToken]`

JSON-bound endpoints are not affected. When the verdict is invalid, those endpoints return `400 Bad Request`. The rules, in order: `Sec-Fetch-Site: same-origin` or `none` passes; an origin trusted by the endpoint's CORS policy passes only if that policy also calls `.AllowCredentials()` (`AllowAnyOrigin` is not honored); `cross-site` or `same-site` is otherwise denied; without `Sec-Fetch-Site`, `Origin` must match the request's scheme, host, and port; a request with neither header passes.

If the pipeline runs `app.UseAntiforgery()`, the token-based result takes precedence and the app keeps its .NET 10 behavior. The header checks decide the outcome when token validation does not run, for example after removing `app.UseAntiforgery()` and `AddAntiforgery()` as the migration guide suggests, or in Blazor SSR apps that don't run the token middleware (see the next entry).

**Fix:**
- **Before removing token-based antiforgery:** keep `app.UseAntiforgery()` if browsers that don't send `Sec-Fetch-Site` must be supported, if the app uses `IAntiforgeryAdditionalDataProvider`, or if compliance requires a separate layer.
- **Legitimate cross-origin form posts:** add a CORS policy for the caller's origin with `.AllowCredentials()`, and apply it to the endpoint (`[EnableCors("policy")]`, `.RequireCors("policy")`, or the default policy).
- **Endpoints that are not CSRF-vulnerable** (non-browser clients, bearer-token authentication): opt out per endpoint with `.DisableAntiforgery()` (Minimal APIs), `[IgnoreAntiforgeryToken]` (MVC), or `@attribute [RequireAntiforgeryToken(false)]` (Blazor SSR page).
- **Custom allow lists:** register your own `ICsrfProtection` implementation.
- **Global opt-out (escape hatch):** set the configuration key `DisableCsrfProtection` to `true` (for example `ASPNETCORE_DisableCsrfProtection=true`). Prefer per-endpoint opt-outs.
- **Reverse proxies:** when a request has no `Sec-Fetch-Site` header, the `Origin` fallback compares against the scheme and host the app sees. If a proxy terminates TLS or rewrites the host, process forwarded headers (`UseForwardedHeaders`) so they match the public URL. Smoke-test form POSTs through the real proxy.

Sources: https://learn.microsoft.com/en-us/aspnet/core/migration/100-to-110, https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

### Blazor server-side rendering defers antiforgery validation to middleware

**Impact: Medium.** The Razor Components endpoint no longer validates form posts by calling `IAntiforgery` itself. It trusts the verdict recorded by `app.UseAntiforgery()` or by the automatic CSRF middleware, and it only generates antiforgery tokens for rendered forms when the token middleware ran for the request. Apps without `app.UseAntiforgery()` are now protected by the `Sec-Fetch-Site`/`Origin` checks and no longer emit tokens.

**Fix:** If the app relies on antiforgery tokens (for example, for browsers that don't send `Sec-Fetch-Site`, or JavaScript that reads the token), call `app.UseAntiforgery()`.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/blazor-server-side-rendering-deferred-cross-site-request-forgery-protection

### Empty validation message placeholders are rendered with `hidden` in static SSR (Preview 7)

**Impact: Low–Medium (for scripts, CSS, and UI tests).** With the new client-side validation for static SSR forms, `<ValidationMessage>` renders an empty placeholder for a valid field: `<div class="validation-message" data-valmsg-for="..." data-valmsg-replace="true" hidden>`. Previously a valid field rendered no element. Interactive render modes are unchanged.

**Fix:** Scripts, CSS selectors, and UI tests that treat the presence of a `.validation-message` element as "the field is invalid" (for example, to set `aria-invalid` or to focus the first invalid field) must use `.validation-message:not([hidden])`. Custom CSS that sets `display` on `.validation-message` overrides `hidden`.

Source: https://github.com/dotnet/aspnetcore/pull/67324

### OpenAPI document includes all ProducesResponseType entries per status code

**Impact: Low–Medium.** ApiExplorer and Minimal API metadata no longer keep only the last `[ProducesResponseType]` or `.Produces<T>()` for a status code. Every declaration is preserved, so the OpenAPI `responses` for a status code can contain several content types or an `anyOf` schema. OpenAPI or Swagger generators that read ApiExplorer and key responses by status code can now fail with a duplicate-key error on actions that declare the same status code twice.

**Fix:** Remove duplicate or stale `[ProducesResponseType]` declarations that were previously masked, update OpenAPI snapshot tests, and check downstream client generators.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/openapi-multiple-produces-per-status

### OpenAPI server URL no longer has a trailing slash when PathBase is empty

**Impact: Low.** The generated server URL is now `https://example.com` instead of `https://example.com/` when `PathBase` is empty.

**Fix:** Update OpenAPI fixtures and contract tests, and check tools that concatenate the server URL with paths.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/openapi-server-url-trailing-slash

### Kestrel tightens HTTP protocol compliance

**Impact: Low–Medium.** HTTP/2 and HTTP/3 requests that contain connection-specific headers (`Connection`, `Proxy-Connection`, `Keep-Alive`, `Transfer-Encoding`, `Upgrade`, or `TE` other than `trailers`) are now rejected as malformed. HTTP/1.1 requests with both `Content-Length` and `Transfer-Encoding` are still processed, but the connection is closed after the response.

**Fix:** Make sure clients, gateways, and HTTP/1.1-to-HTTP/2 proxies strip connection-specific headers, and that clients send only one of `Content-Length` or `Transfer-Encoding`.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/kestrel-strict-protocol-compliance

### Hosting emits OpenTelemetry HTTP semantic-convention tags by default

**Impact: Low.** The server `Activity` now carries OpenTelemetry HTTP tags by default (`http.request.method`, `http.route`, `url.path`, and others). For conventional routes, `http.route` contains resolved values such as `Store/Checkout/{id?}` instead of the raw template, and `Activity.StatusDescription` is no longer set on unhandled exceptions.

**Fix:** Upgrade `OpenTelemetry.Instrumentation.AspNetCore` if tags are duplicated, and update enrichers or dashboards that depended on the old route format. `AppContext.SetSwitch("Microsoft.AspNetCore.Hosting.SuppressActivityOpenTelemetryData", true)` restores only the old tag behavior.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/http-activity-otel-semconv

### Response compression always emits Vary: Accept-Encoding

**Impact: Low.** The response compression middleware now adds `Vary: Accept-Encoding` to every response that passes through it, not only compressed ones.

**Fix:** Usually none. Make sure caches and CDNs normalize `Accept-Encoding`.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/response-compression-always-vary

### Passkey sign-in enforces email/phone confirmation and lockout

**Impact: Low (Identity with passkeys).** `SignInManager<TUser>.PasskeySignInAsync` now runs the same pre-sign-in checks as password sign-in, and returns `SignInResult.NotAllowed` or `SignInResult.LockedOut` for unconfirmed or locked-out users.

**Fix:** Handle both results in the sign-in UI, as for password sign-in.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/passkey-signin-enforces-confirmation-lockout

### Blazor enhanced navigation no longer preloads resources

**Impact: Low.** `<link rel="preload">` and `<link rel="modulepreload">` elements for framework resources are now emitted only for full page loads, not for enhanced navigation responses.

**Fix:** Add explicit preload links in `<HeadContent>` for resources a page needs after enhanced navigation.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/blazor-enhanced-nav-preloading-disabled

### Blazor custom event registration throws when the name matches a browser event

**Impact: Low.** `Blazor.registerCustomEventType(name, { browserEventName })` now throws when `name` equals `browserEventName`. Previously each browser event invoked the handler twice.

**Fix:** Rename the custom event (for example with a library prefix) and update the matching `[EventHandler]` attribute and `@on...` usages.

Source: https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/11/blazor-custom-event-name-collision

### Blazor Virtualize&lt;T&gt; default OverscanCount changed from 3 to 15 (Preview 3)

**Impact: Low.** The default `OverscanCount` on the `Virtualize<TItem>` component changed from `3` to `15` to support variable-height item measurement. `QuickGrid` retains its own default of `3`.

**Fix:** If performance-sensitive, set `OverscanCount` explicitly: `<Virtualize OverscanCount="3" />`.

Source: https://github.com/dotnet/aspnetcore/pull/64964
