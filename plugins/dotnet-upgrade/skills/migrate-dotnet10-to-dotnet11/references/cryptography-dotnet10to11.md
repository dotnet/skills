# Cryptography Breaking Changes (.NET 11)

These breaking changes affect projects using cryptography APIs. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

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

### AIA certificate downloads disabled by default during client-certificate validation (Preview 3)

**Impact: Medium.** AIA (Authority Information Access) certificate downloads are now disabled by default when performing server-side client-certificate chain validation. Previously the runtime would attempt to fetch intermediate CA certificates online.

**Fix:** If using mTLS where client certificates rely on AIA URLs for intermediate CAs, either:
- Pre-install the full certificate chain on the server
- Have clients send the full chain including intermediates
- Re-enable AIA downloads via `X509ChainPolicy.DisableCertificateDownloads = false`

Source: https://github.com/dotnet/runtime/pull/125049

### HKDF on Windows uses the CNG implementation (Preview 1)

**Impact: Low (Windows only).** `HKDF.DeriveKey` and `HKDF.Expand` now throw `CryptographicException` on Windows when the input keying material or pseudorandom key is larger than 2,048 bytes.

**Fix:** Keep HKDF inputs at or below 2,048 bytes.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/11/hkdf-windows-cng

### Linux AIA certificate fetching limited to two fetches per chain build (Preview 7)

**Impact: Low–Medium (Linux and other OpenSSL platforms).** `X509Chain.Build` now performs at most two AIA downloads per chain build, matching Windows. Chains that need more downloads to complete now fail.

**Fix:** Supply intermediate certificates through `X509ChainPolicy.ExtraStore`, or install them in the certificate store.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/11/aia-fetch-limit-linux

## Networking

### HttpClient high-cardinality metrics use observable counters (RC 1)

**Impact: Low.** The `http.client.open_connections` and `http.client.active_requests` instruments are now `ObservableUpDownCounter` instead of `UpDownCounter`. OpenTelemetry, Prometheus exporters, `dotnet-counters`, and `dotnet-monitor` are not affected.

**Fix:** Code that listens with a `MeterListener` directly must call `RecordObservableInstruments()` periodically to receive these measurements.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/11/http-metrics-observable-counters
