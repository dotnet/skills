# Cryptography Breaking Changes (.NET 9)

These changes affect projects using `System.Security.Cryptography`, X.509 certificates, or OpenSSL.

## Source-Incompatible Changes

### X509Certificate2 and X509Certificate constructors are obsolete (SYSLIB0057)

**Impact: High.** The constructors on `X509Certificate` and `X509Certificate2` that accept content as `byte[]`, `ReadOnlySpan<byte>`, or a file path are now obsolete. The `Import` methods on `X509Certificate2Collection` are also obsolete. Using them produces warning `SYSLIB0057`.

```csharp
// BREAKS — SYSLIB0057 warning
var cert = new X509Certificate2(certBytes);
var cert2 = new X509Certificate2("cert.pfx", "password");
collection.Import(certBytes);
```

**Why:** These APIs accepted multiple formats (X.509, PKCS7, PKCS12/PFX) from a single parameter, making it possible to load a different format than intended with user-supplied data.

**Fix:** Use `X509CertificateLoader` methods:
```csharp
// Load DER/PEM encoded certificate
var derCert = X509CertificateLoader.LoadCertificate(certBytes);

// Load PFX/PKCS12
var pfxCert = X509CertificateLoader.LoadPkcs12(pfxBytes, "password");

// Load from file
var fileCert = X509CertificateLoader.LoadCertificateFromFile("cert.pem");
var filePfxCert = X509CertificateLoader.LoadPkcs12FromFile("cert.pfx", "password");
```

### APIs removed from System.Security.Cryptography.Pkcs netstandard2.0

Some APIs were removed from the `netstandard2.0` target of the `System.Security.Cryptography.Pkcs` package. These APIs are still available when targeting .NET.

## Behavioral Changes

### SafeEvpPKeyHandle.DuplicateHandle up-refs the handle

**Impact: Low.** `SafeEvpPKeyHandle.DuplicateHandle` now increments the reference count on the underlying OpenSSL key handle instead of creating a fully independent copy. This is more efficient but means changes to one handle may affect duplicates.

### Windows private key lifetime simplified

**Impact: Low.** The lifetime management of private keys associated with certificates on Windows has been simplified. This may affect code that makes assumptions about when private key handles are released.
