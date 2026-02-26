# Cryptography Breaking Changes (.NET 10)

These changes affect projects using `System.Security.Cryptography`, X.509 certificates, or OpenSSL.

## Source-Incompatible Changes

### MLDsa and SlhDsa 'SecretKey' members renamed to 'PrivateKey'

All members named `SecretKey` on `MLDsa` and `SlhDsa` types have been renamed to `PrivateKey`. Update all references:

```csharp
// Before
var key = mlDsa.SecretKey;

// After
var key = mlDsa.PrivateKey;
```

### CoseSigner.Key can be null

`CoseSigner.Key` is now nullable (`AsymmetricAlgorithm?`). Code that assumes it's non-null needs null checks:

```csharp
// Before
var algorithm = signer.Key.SignatureAlgorithm;

// After
var algorithm = signer.Key?.SignatureAlgorithm
    ?? throw new InvalidOperationException("Key is null");
```

### X509Certificate and PublicKey key parameters can be null

`X509Certificate.GetKeyAlgorithmParameters()` and `PublicKey.EncodedParameters` can now return null. Add null checks where these values are consumed.

## Behavioral Changes

### OpenSSL 1.1.1 or later required on Unix

.NET 10 requires OpenSSL 1.1.1+ on Unix systems. Older OpenSSL versions are no longer supported. Check with:
```bash
openssl version
```

### OpenSSL cryptographic primitives aren't supported on macOS

Using OpenSSL-specific cryptographic primitives on macOS is no longer supported. Use the platform's native cryptography (Apple Security framework) instead.

### X500DistinguishedName validation is stricter

`X500DistinguishedName` now validates input more strictly. Malformed distinguished names that were previously accepted may now throw exceptions.

### CompositeMLDsa updated to draft-08

The Composite ML-DSA implementation has been updated to align with draft-08. Key and signature formats from earlier drafts are incompatible.

### Environment variable renamed to DOTNET_OPENSSL_VERSION_OVERRIDE

If you use the OpenSSL version override environment variable, update from the old name to `DOTNET_OPENSSL_VERSION_OVERRIDE`.
