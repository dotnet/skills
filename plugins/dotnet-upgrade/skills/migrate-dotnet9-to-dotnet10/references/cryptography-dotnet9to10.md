# Cryptography Breaking Changes (.NET 10)

These changes affect projects using `System.Security.Cryptography`, X.509 certificates, or OpenSSL.

## Source-Incompatible Changes

### MLDsa and SlhDsa 'SecretKey' members renamed to 'PrivateKey'

All members containing `SecretKey` on `MLDsa` and `SlhDsa` types have been renamed to use `PrivateKey`. This affects methods and properties:

```csharp
// Before
int size = key.Algorithm.SecretKeySizeInBytes;
byte[] output = new byte[size];
key.ExportMLDsaSecretKey(output);
key.ImportMLDsaSecretKey(data);

// After
int size = key.Algorithm.PrivateKeySizeInBytes;
byte[] output = new byte[size];
key.ExportMLDsaPrivateKey(output);
key.ImportMLDsaPrivateKey(data);
// Same pattern for SlhDsa: ExportSlhDsaSecretKey → ExportSlhDsaPrivateKey, etc.
```

### Rfc2898DeriveBytes constructors are obsolete (SYSLIB0060)

All `Rfc2898DeriveBytes` constructors are now obsolete. Use the static `Rfc2898DeriveBytes.Pbkdf2` method instead:

```csharp
// Before
using var deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
byte[] key = deriveBytes.GetBytes(32);

// After
byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
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

### Environment variable renamed from CLR_OPENSSL_VERSION_OVERRIDE to DOTNET_OPENSSL_VERSION_OVERRIDE

If you use `CLR_OPENSSL_VERSION_OVERRIDE` to specify the preferred OpenSSL library version on Linux, rename it to `DOTNET_OPENSSL_VERSION_OVERRIDE`.
