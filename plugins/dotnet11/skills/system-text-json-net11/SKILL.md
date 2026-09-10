---
name: system-text-json-net11
description: >
  Imperative guidance for the System.Text.Json APIs added in .NET 11: the built-in
  `JsonNamingPolicy.PascalCase` naming policy, and the strongly-typed
  `JsonSerializerOptions.GetTypeInfo<T>()` and
  `JsonSerializerOptions.TryGetTypeInfo<T>(out JsonTypeInfo<T>? info)`
  metadata accessors.
  USE ONLY when the user is targeting net11.0 or later and needs
  PascalCase JSON property or dictionary-key names without writing a custom naming policy, a strongly-typed
  `JsonTypeInfo<T>` instead of the non-generic `JsonTypeInfo`, or a no-throw way to probe
  whether a type's serialization metadata is resolved.
  DO NOT USE when the target is earlier than net11.0, the requested behavior uses an
  established pre-net11 naming policy, or the user explicitly selected another JSON library.
license: MIT
---

# System.Text.Json — .NET 11

Three APIs were added to `System.Text.Json` in .NET 11. This skill tells you exactly
when to reach for each one, what to write, what **not** to write, and how to prove the
result runs. Do not describe these APIs to the user — apply them, then run the code and
show the output.

| API | Replaces the pre-.NET-11 workaround of... |
| --- | --- |
| `JsonNamingPolicy.PascalCase` (static property) | writing a custom `JsonNamingPolicy` subclass or hand-annotating every member with `[JsonPropertyName]` |
| `JsonSerializerOptions.GetTypeInfo<T>()` | calling non-generic `GetTypeInfo(typeof(T))` and casting to `JsonTypeInfo<T>` |
| `JsonSerializerOptions.TryGetTypeInfo<T>(out JsonTypeInfo<T>? info)` | wrapping `GetTypeInfo` in `try`/`catch` to probe availability |

## Step 0 — Make the requested `net11.0` validation possible

These APIs only exist in the .NET 11 base class library. Before writing code:

1. Run `dotnet --list-sdks` and confirm an SDK that can target `net11.0` is present — an
   `11.x` SDK, or a later SDK with the `net11.0` targeting pack.
2. If the user explicitly asks you to run the sample and no suitable SDK is installed,
   use the official `dotnet-install` script to install the current .NET 11 SDK into a
   temporary or project-local directory. Prefer the GA channel build. Use a preview only
   when GA is not yet available or the user explicitly requested a preview. Do not require
   administrator access, change the machine-wide `PATH`, or replace an installed SDK.
3. Run the sample with that local `dotnet` executable. If download or execution is blocked,
   still provide the complete `net11.0` program and report that it was **not run**. Never
   substitute `net10.0`, a custom naming policy, or a different API and present that as
   validation of the .NET 11 feature.

Use the channel, not a guessed version. Try the GA channel first:

```powershell
$installScript = Join-Path $env:TEMP "dotnet-install-$([guid]::NewGuid()).ps1"
try {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
    & $installScript -Channel 11.0 -InstallDir .\.dotnet
    & .\.dotnet\dotnet.exe run --project <PATH_TO_NET11_PROJECT>
}
finally {
    Remove-Item -LiteralPath $installScript -Force -ErrorAction SilentlyContinue
}
```

```bash
install_script="$(mktemp "${TMPDIR:-/tmp}/dotnet-install.XXXXXX")"
trap 'rm -f "$install_script"' EXIT
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$install_script"
bash "$install_script" --channel 11.0 --install-dir ./.dotnet
./.dotnet/dotnet run --project <PATH_TO_NET11_PROJECT>
```

Before .NET 11 GA, retry the install with `-Quality preview` (PowerShell) or
`--quality preview` (shell).

## Decision table — symptom → do this → never do this

Match the user's request to a row, apply the **Do this** cell verbatim, and confirm the
**Verify** column before you are done.

| User asks for… | Do this (on `net11.0`) | Never do this | Verify |
| --- | --- | --- | --- |
| PascalCase JSON property names | `options.PropertyNamingPolicy = JsonNamingPolicy.PascalCase;` | define `class …: JsonNamingPolicy`; add per-member `[JsonPropertyName]`; string-case the names yourself | output JSON keys are PascalCase — e.g. `"Name"`, `"Age"` |
| PascalCase dictionary keys | `options.DictionaryKeyPolicy = JsonNamingPolicy.PascalCase;` | set only `PropertyNamingPolicy`; pre-transform the dictionary; define a custom policy | dictionary keys such as `pendingOrders` become `"PendingOrders"` |
| Strongly-typed metadata `JsonTypeInfo<T>` | set `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`, then `JsonTypeInfo<T> ti = options.GetTypeInfo<T>();` | `(JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))` | variable is typed `JsonTypeInfo<T>`, no cast |
| Probe whether metadata is resolved | `if (options.TryGetTypeInfo<T>(out var ti)) { … } else { … }` | `try { options.GetTypeInfo<T>(); } catch (…) { … }` | no `try`/`catch`; both branches handled |

## Rule 1 — PascalCase property names

**When** the user wants JSON output whose property names are PascalCase (`Name`, `Age`)
and asks for the built-in / framework-provided way:

1. Create or reuse a `JsonSerializerOptions` and set
   `PropertyNamingPolicy = JsonNamingPolicy.PascalCase`.
2. Serialize with those options.

Do **not** write a `JsonNamingPolicy` subclass, do **not** add `[JsonPropertyName("…")]`
attributes to force casing, and do **not** upper-case the first letter of each name by
hand. `JsonNamingPolicy.PascalCase` is the single correct answer on .NET 11.

```csharp
// Console project (reflection enabled by default). To run this as a file-based app
// (dotnet run app.cs), also set TypeInfoResolver = new DefaultJsonTypeInfoResolver()
// — see "Producing runnable output" below.
using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase
};
string json = JsonSerializer.Serialize(new { name = "Jane", age = 30 }, options);
Console.WriteLine(json);
// {"Name":"Jane","Age":30}
```

### Dictionary keys are a separate setting

`PropertyNamingPolicy` does not transform `Dictionary<string, TValue>` keys. For that
request, set `DictionaryKeyPolicy`:

```csharp
var options = new JsonSerializerOptions
{
    DictionaryKeyPolicy = JsonNamingPolicy.PascalCase
};
var values = new Dictionary<string, int>
{
    ["pendingOrders"] = 2,
    ["activeUsers"] = 5
};
Console.WriteLine(JsonSerializer.Serialize(values, options));
// {"PendingOrders":2,"ActiveUsers":5}
```

## Rule 2 — Strongly-typed `JsonTypeInfo<T>`

**When** the user wants type metadata back as `JsonTypeInfo<T>` (not the non-generic
`JsonTypeInfo` that needs a cast):

1. Call `options.GetTypeInfo<T>()` — it returns `JsonTypeInfo<T>` directly.
2. Assign it to a `JsonTypeInfo<T>` variable and use it (e.g. pass it to
   `JsonSerializer.Serialize`/`Deserialize`).

Do **not** call the non-generic `GetTypeInfo(Type)` overload and cast the result.

> **Requires a resolver.** `GetTypeInfo<T>()` throws `NotSupportedException`
> (`NoMetadataForType`) unless the options have a `TypeInfoResolver` — set
> `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` for reflection-based apps, or use
> a source-generated `JsonSerializerContext` for trimmed/AOT apps.

```csharp
// File-based app (run: dotnet run app.cs). In a .csproj project, remove this line and
// set <TargetFramework>net11.0</TargetFramework> in the project file instead.
#:property TargetFramework=net11.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

JsonTypeInfo<Person> typeInfo = options.GetTypeInfo<Person>();
Console.WriteLine(typeInfo.Type.Name); // Person

record Person(string Name, int Age);
```

## Rule 3 — Probe metadata without throwing

**When** the user wants to check whether metadata for `T` is available and branch on it —
*without* an exception being thrown when it is not:

1. Call `options.TryGetTypeInfo<T>(out var info)`.
2. Handle the `true` branch (metadata resolved, use `info`) and the `false` branch
   (not resolved) explicitly.

Do **not** wrap `GetTypeInfo<T>()` in `try`/`catch` to detect the missing case — that is
exactly the anti-pattern this API removes. `TryGetTypeInfo<T>` returns `false` (instead of
throwing) when no resolver can produce metadata for `T`, which is precisely the case you
want to branch on.

```csharp
// File-based app (run: dotnet run app.cs). In a .csproj project, remove this line and
// set <TargetFramework>net11.0</TargetFramework> in the project file instead.
#:property TargetFramework=net11.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

// Configured with a resolver → metadata is available.
var configured = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};
if (configured.TryGetTypeInfo<Person>(out JsonTypeInfo<Person>? info) && info is not null)
{
    Console.WriteLine($"Resolved: {info.Type.Name}"); // Resolved: Person
}
else
{
    Console.WriteLine("Type info not available");
}

// No resolver → TryGetTypeInfo returns false instead of throwing.
var empty = new JsonSerializerOptions();
Console.WriteLine(empty.TryGetTypeInfo<Person>(out _)); // False

record Person(string Name, int Age);
```

## Producing runnable output on `net11.0`

The task is not done until the program runs on `net11.0` and prints its JSON. Prefer a
console **project** — reflection-based serialization works there out of the box. A
file-based app also works but has one important caveat (below).

When the installed SDK cannot target `net11.0` and execution was requested, install an
SDK locally with the official script and invoke it by full path. The install script is
non-administrative and does not persistently alter `PATH`.

### Option A — console project (recommended)

Create a project whose `.csproj` contains `<TargetFramework>net11.0</TargetFramework>`,
put the code in `Program.cs`, then run `dotnet run`. Confirm the process exits with code 0
and prints the expected JSON.

```csharp
using System.Text.Json;

var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.PascalCase };
Console.WriteLine(JsonSerializer.Serialize(new { name = "Jane", age = 30 }, options));
// {"Name":"Jane","Age":30}
```

### Option B — file-based app (quickest, one caveat)

Save as `app.cs`, then run `dotnet run app.cs`; the first directive pins the framework.

> **Caveat — file-based apps disable System.Text.Json reflection.** In a `dotnet run app.cs`
> file-based app, `JsonSerializer.IsReflectionEnabledByDefault` is `false`, so plain
> reflection serialization throws `NotSupportedException` (`NoMetadataForType`). Set an
> explicit `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` on the options (as below),
> or use a source-generated `JsonSerializerContext`. A regular project does **not** need this.

```csharp
// File-based app (run: dotnet run app.cs). In a .csproj project, remove this line and
// set <TargetFramework>net11.0</TargetFramework> in the project file instead.
#:property TargetFramework=net11.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};
Console.WriteLine(JsonSerializer.Serialize(new { name = "Jane", age = 30 }, options));
// {"Name":"Jane","Age":30}
```

## Worked example — serialize with typed metadata + PascalCase

The record below uses lowercase member names on purpose, so the PascalCase policy
visibly rewrites them in the output:

```csharp
// File-based app (run: dotnet run app.cs). In a .csproj project, remove this line and
// set <TargetFramework>net11.0</TargetFramework> in the project file instead.
#:property TargetFramework=net11.0

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

JsonTypeInfo<Person> typeInfo = options.GetTypeInfo<Person>();
string json = JsonSerializer.Serialize(new Person("Jane", 30), typeInfo);
Console.WriteLine(json);
// {"Name":"Jane","Age":30}

record Person(string name, int age);
```

## Validation checklist

Before reporting success, confirm every applicable box:

- [ ] The project or file-based app targets `net11.0` (visible in the `.csproj` or the
      `#:property TargetFramework=net11.0` directive).
- [ ] PascalCase requests use `JsonNamingPolicy.PascalCase` — no custom `JsonNamingPolicy`
      subclass and no per-member `[JsonPropertyName]` attributes just to change casing.
- [ ] Dictionary-key requests set `DictionaryKeyPolicy`, not only `PropertyNamingPolicy`.
- [ ] Typed-metadata requests use the generic `GetTypeInfo<T>()` — no cast of a
      non-generic `JsonTypeInfo` — and the options set a `TypeInfoResolver` (e.g.
      `DefaultJsonTypeInfoResolver`) so the call doesn't throw `NoMetadataForType`.
- [ ] Probing requests use `TryGetTypeInfo<T>(out …)` — no `try`/`catch` around
      `GetTypeInfo`.
- [ ] The program was actually run (`dotnet run …`), exited 0, and its printed JSON shows
      the expected property names (e.g. `"Name"`, `"Age"`).
- [ ] If a **file-based app** (`dotnet run app.cs`) is used, every `JsonSerializerOptions`
      sets a `TypeInfoResolver` — file-based apps disable reflection so plain serialization
      throws `NoMetadataForType` without one.

## Common pitfalls

| Pitfall | Fix |
| --- | --- |
| Hand-rolling a `class … : JsonNamingPolicy` for PascalCase | Delete it; set `PropertyNamingPolicy = JsonNamingPolicy.PascalCase`. |
| Adding `[JsonPropertyName("Name")]` to every member to force casing | Remove the attributes; the naming policy handles all members at once. |
| Casting `(JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))` | Call the generic `options.GetTypeInfo<T>()`; no cast needed. |
| `try { options.GetTypeInfo<T>(); } catch (…) { … }` to test availability | Replace with `if (options.TryGetTypeInfo<T>(out var info)) { … }`. |
| `NotSupportedException` / `NoMetadataForType` from `GetTypeInfo<T>()` | The options have no resolver. Set `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` (reflection) or a source-generated `JsonSerializerContext` (trim/AOT). |
| `NoMetadataForType` even for a plain `Serialize` in a `dotnet run app.cs` file-based app | File-based apps disable STJ reflection. Add `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`, or run it as a normal project instead. |
| Leaving the app on the SDK's default TFM | Pin `net11.0` explicitly so the .NET 11 APIs resolve and the output shows the target. |
| Claiming success without running | Run `dotnet run` and paste the actual JSON output; the target is a working, executed program. |

## More info

- [JsonNamingPolicy class](https://learn.microsoft.com/dotnet/api/system.text.json.jsonnamingpolicy) — built-in naming policies including `PascalCase`
- [JsonSerializerOptions.GetTypeInfo](https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializeroptions.gettypeinfo) — typed and non-typed metadata access
- [JsonTypeInfo\<T\>](https://learn.microsoft.com/dotnet/api/system.text.json.serialization.metadata.jsontypeinfo-1) — strongly-typed serialization metadata
- [DefaultJsonTypeInfoResolver](https://learn.microsoft.com/dotnet/api/system.text.json.serialization.metadata.defaultjsontypeinforesolver) — reflection-based resolver required by `GetTypeInfo`/`TryGetTypeInfo`
- [File-based apps](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps) — `dotnet run app.cs` and the `#:property` directive
