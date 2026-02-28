# Cryptography Breaking Changes (.NET 11)

These breaking changes affect projects using cryptography APIs. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional cryptography breaking changes are expected in later previews.

## Behavioral Changes

### DSA removed from macOS

**Impact: Medium (macOS only).** DSA (Digital Signature Algorithm) has been removed from macOS. Code that uses DSA for signing or verification will throw on macOS.

```csharp
// BREAKS on macOS in .NET 11
using var dsa = DSA.Create();
var signature = dsa.SignData(data, HashAlgorithmName.SHA256);

// FIX: Use a different algorithm
using var ecdsa = ECDsa.Create();
var signature = ecdsa.SignData(data, HashAlgorithmName.SHA256);
```

**Fix:** Migrate from DSA to a more modern algorithm:
- **ECDSA** — recommended replacement for digital signatures
- **RSA** — alternative if ECDSA is not suitable
- **Ed25519** — if available in your scenario

This change only affects macOS. DSA continues to work on Windows and Linux (though it is generally considered a legacy algorithm).
