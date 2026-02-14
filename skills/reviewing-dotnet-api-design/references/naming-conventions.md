# Naming Conventions Reference

## Casing

- **PascalCase**: all public identifiers except parameters
- **camelCase**: parameters, locals, private fields with `_` prefix, static fields with `s_` prefix

## Acronym Casing

| Acronym | Example | Pattern |
|---------|--------|---------|
| IO | `System.IO` | Two letters → uppercase |
| UI | `UIElement` | Two letters → uppercase |
| DB | `DbConnection` | Two letters → uppercase (note: newer APIs use `Db`) |
| Html | `HtmlWriter` | Three letters → PascalCase |
| Xml | `XmlReader` | Three letters → PascalCase |
| Json | `JsonSerializer` | Four letters → PascalCase |
| Url | `UrlEncoder` | Three letters → PascalCase |

## Type Name Suffixes

| When type... | Suffix | Examples |
|-------------|--------|-------------|
| Derives from `Exception` | `Exception` | `ArgumentNullException`, `IOException` |
| Derives from `Attribute` | `Attribute` | `ObsoleteAttribute`, `SerializableAttribute` |
| Derives from `EventArgs` | `EventArgs` | `CancelEventArgs`, `PropertyChangedEventArgs` |
| Represents a collection | `Collection` | `ObservableCollection<T>`, `KeyedCollection<K,T>` |
| Represents a dictionary | `Dictionary` | `ConcurrentDictionary<K,V>`, `SortedDictionary<K,V>` |

## Method Names

Async methods add `Async` suffix. Verbs or verb phrases.

## Property Names

Boolean properties use `Is`/`Can`/`Has` prefix.

## Event Names

| Pre-event | Post-event |
|-----------|-----------|
| `Closing` | `Closed` |
| `Validating` | `Validated` |
| `PropertyChanging` | `PropertyChanged` |
| `CollectionChanging` | N/A (some types omit pre-event) |

## Enum Names

- Non-flag enums: singular nouns
- Flag enums: plural nouns with `[Flags]`

## Namespace Patterns

`<Company>.<Technology>[.<Feature>]`

```
System.Collections.Generic
System.IO.Compression
System.Net.Http
System.Text.Json
Microsoft.Extensions.Logging
Microsoft.Extensions.DependencyInjection
```

## What to Avoid

- Hungarian notation (`strName`, `iCount`, `bEnabled`)
- Underscores in public names (`Get_Value`, `Max_Count`)
- Abbreviations (`Btn`, `Msg`, `Mgr` — except universally known ones like `IO`)
- Names differing only by case
- Language-specific type names in methods (`GetInt` vs `GetInt32`)
