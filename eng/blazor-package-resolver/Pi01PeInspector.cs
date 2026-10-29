using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace BlazorPackageResolver;

internal sealed record Pi01PeInspection(string State, string? Detail);

internal static class Pi01PeInspector
{
    internal static Pi01PeInspection Inspect(ReadOnlyMemory<byte> data)
    {
        if (data.Length > 64 * 1024 * 1024)
            return new("unsafe", "PE image exceeds supported size.");
        try
        {
            var (state, detail) = InspectCore(data.ToArray());
            return new(state, detail);
        }
        catch (Exception error) when (error is BadImageFormatException or InvalidDataException
                                      or ArgumentException or IndexOutOfRangeException
                                      or CryptographicException or AsnContentException
                                      or InvalidOperationException or NotSupportedException
                                      or OverflowException)
        {
            return new("unsafe", error.Message);
        }
    }

private static (string state, string? detail) InspectCore(byte[] data)
{
    using var pe = new PEReader(new MemoryStream(data));
    if (!pe.HasMetadata)
    {
        if (pe.PEHeaders.CorHeader is not null)
            throw new BadImageFormatException("Managed metadata absent.");
        _ = pe.PEHeaders.PEHeader ?? throw new BadImageFormatException("PE header absent.");
        return ("native-pe", null);
    }
    var peHeader = pe.PEHeaders.PEHeader ?? throw new BadImageFormatException("PE header absent.");
    int headersToSign = pe.PEHeaders.PEHeaderStartOffset +
        pe.PEHeaders.CoffHeader.SizeOfOptionalHeader +
        pe.PEHeaders.CoffHeader.NumberOfSections * 40;
    if (headersToSign > peHeader.SizeOfHeaders)
        throw new BadImageFormatException("PE section headers exceed declared header size.");
    if (data.AsSpan(headersToSign, peHeader.SizeOfHeaders - headersToSign).ContainsAnyExcept((byte)0))
        return ("opaque-container", "Uninventoried bytes in PE header alignment padding.");
    int lastSectionEnd = peHeader.SizeOfHeaders;
    foreach (var section in pe.PEHeaders.SectionHeaders.OrderBy(item => item.PointerToRawData))
    {
        if (section.SizeOfRawData == 0)
            continue;
        if (section.PointerToRawData < lastSectionEnd || section.SizeOfRawData < 0 ||
            section.PointerToRawData > data.Length - section.SizeOfRawData)
            throw new BadImageFormatException("PE section overlaps headers, another section, or end of assembly.");
        if (data.AsSpan(lastSectionEnd, section.PointerToRawData - lastSectionEnd)
                .ContainsAnyExcept((byte)0))
            return ("opaque-container", "Uninventoried bytes between PE sections.");
        lastSectionEnd = section.PointerToRawData + section.SizeOfRawData;
    }
    int certificateDirectory = pe.PEHeaders.PEHeaderStartOffset +
        (peHeader.Magic == PEMagic.PE32 ? 128 : 144);
    int certificateOffset = BitConverter.ToInt32(data, certificateDirectory);
    int certificateSize = BitConverter.ToInt32(data, certificateDirectory + 4);
    if (certificateOffset == 0 && certificateSize == 0)
    {
        if (lastSectionEnd != data.Length)
            return ("opaque-container", $"Uninventoried PE overlay: {data.Length - lastSectionEnd} bytes after sections.");
    }
    else
    {
        if (certificateOffset != lastSectionEnd || certificateSize <= 0 ||
            certificateOffset % 8 != 0 || certificateSize % 8 != 0 ||
            certificateSize > data.Length - certificateOffset)
            throw new BadImageFormatException("Invalid Authenticode certificate table boundary.");
        ValidateCertificates(data.AsMemory(certificateOffset, certificateSize));
        if (certificateOffset + certificateSize != data.Length)
            return ("opaque-container", $"Uninventoried PE overlay: {data.Length - certificateOffset - certificateSize} bytes after certificate table.");
    }
    var metadata = pe.GetMetadataReader();
    ValidateMetadataTables(pe, metadata);
    if (metadata.GetTableRowCount(TableIndex.Module) != 1)
        throw new BadImageFormatException("Module definition missing or ambiguous.");
    var module = metadata.GetModuleDefinition();
    if (module.Name.IsNil || string.IsNullOrWhiteSpace(metadata.GetString(module.Name)) ||
        module.Mvid.IsNil || metadata.GetGuid(module.Mvid) == Guid.Empty ||
        metadata.TypeDefinitions.Count == 0 ||
        metadata.GetString(metadata.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(1)).Name) != "<Module>")
        throw new BadImageFormatException("Module metadata is empty or incoherent.");
    if (!metadata.IsAssembly)
    {
        if (metadata.GetTableRowCount(TableIndex.Assembly) != 0)
            throw new BadImageFormatException("Assembly table is inconsistent.");
        var moduleHeader = pe.PEHeaders.CorHeader ??
            throw new BadImageFormatException("CLI header absent.");
        var reservation = moduleHeader.StrongNameSignatureDirectory;
        if ((moduleHeader.Flags & CorFlags.StrongNameSigned) != 0 ||
            reservation.RelativeVirtualAddress != 0 || reservation.Size != 0)
            throw new BadImageFormatException("Module-only image has strong-name signing metadata.");
        return ("module-only", null);
    }
    if (metadata.GetTableRowCount(TableIndex.Assembly) != 1)
        throw new BadImageFormatException("Assembly definition is ambiguous.");
    var assembly = metadata.GetAssemblyDefinition();
    if (assembly.Name.IsNil || string.IsNullOrWhiteSpace(metadata.GetString(assembly.Name)))
        throw new BadImageFormatException("Assembly name is absent.");
    var key = assembly.PublicKey.IsNil ? [] : metadata.GetBlobBytes(assembly.PublicKey);
    if (((assembly.Flags & AssemblyFlags.PublicKey) != 0) != (key.Length > 0))
        throw new BadImageFormatException("Assembly public-key flag and blob disagree.");
    var header = pe.PEHeaders.CorHeader ?? throw new BadImageFormatException("CLI header absent.");
    var signature = header.StrongNameSignatureDirectory;
    bool flag = (header.Flags & CorFlags.StrongNameSigned) != 0;
    if (signature.Size == 0 && signature.RelativeVirtualAddress == 0)
    {
        if (flag || key.Length > 0)
            throw new BadImageFormatException("Public key/flag without signature reservation.");
        return ("unsigned", null);
    }
    if (signature.Size <= 0 || signature.Size > 4096 || signature.RelativeVirtualAddress == 0)
        throw new BadImageFormatException("Signature reservation is incomplete.");
    var start = FileOffset(pe, signature.RelativeVirtualAddress, signature.Size);
    var signedBytes = data.AsSpan(start, signature.Size).ToArray();
    if (!signedBytes.Any(value => value != 0))
        return (flag ? "public-signed" : "delay-signed", null);
    if (!flag)
        throw new BadImageFormatException("Signature bytes present with signed flag clear.");
    if (key.Length == 0)
        throw new BadImageFormatException("Signed assembly lacks a public key.");
    if (key.Length < 32)
        return ("signature-unverified", $"Unsupported strong-name public-key blob length={key.Length}; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}.");
    uint sigAlg = BitConverter.ToUInt32(key, 0), hashAlg = BitConverter.ToUInt32(key, 4);
    uint blobSize = BitConverter.ToUInt32(key, 8);
    if (sigAlg != 0x2400 || hashAlg is not (0x8004 or 0x800c) ||
        blobSize != key.Length - 12 || key[12] != 6 || key[13] != 2 ||
        BitConverter.ToUInt32(key, 16) is not (0x2400 or 0xa400) ||
        BitConverter.ToUInt32(key, 20) != 0x31415352)
        return ("signature-unverified", $"Unsupported strong-name algorithm/key shape: signature=0x{sigAlg:x}, hash=0x{hashAlg:x}, blob={blobSize}; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}.");
    uint bitLength = BitConverter.ToUInt32(key, 24);
    if (bitLength < 1024 || bitLength > 32768 || bitLength % 8 != 0 ||
        key.Length != 32 + bitLength / 8 || signature.Size != bitLength / 8)
        return ("signature-unverified", $"Unsupported RSA modulus/reservation: bits={bitLength}, blob={key.Length}, reservation={signature.Size}; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}.");
    int bits = (int)bitLength;
    var exponent = BitConverter.ToUInt32(key, 28);
    if (exponent < 3 || exponent % 2 == 0)
        return ("signature-unverified", $"Unsupported RSA public exponent: {exponent}; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}.");
    byte[] modulus = key.AsSpan(32, bits / 8).ToArray();
    Array.Reverse(modulus);
    using var rsa = RSA.Create();
    try
    {
        rsa.ImportParameters(new RSAParameters {
            Exponent = BitConverter.GetBytes(exponent).Reverse().SkipWhile(value => value == 0).ToArray(),
            Modulus = modulus,
        });
    }
    catch (CryptographicException)
    {
        return ("signature-unverified", $"Unsupported RSA key material; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}.");
    }
    byte[] image = (byte[])data.Clone();
    int optional = pe.PEHeaders.PEHeaderStartOffset;
    Array.Clear(image, optional + 64, 4); // PE checksum is not signed.
    Array.Clear(image, optional + (peHeader.Magic == PEMagic.PE32 ? 128 : 144), 8); // Authenticode directory is not signed.
    using var hash = IncrementalHash.CreateHash(hashAlg == 0x8004 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256);
    hash.AppendData(image, 0, headersToSign);
    bool foundSignature = false;
    foreach (var section in pe.PEHeaders.SectionHeaders.OrderBy(item => item.PointerToRawData))
    {
        if (section.PointerToRawData < 0 || section.SizeOfRawData < 0 ||
            section.PointerToRawData > image.Length - section.SizeOfRawData)
            throw new BadImageFormatException("PE section falls outside the assembly.");
        int sectionStart = section.PointerToRawData, sectionEnd = sectionStart + section.SizeOfRawData;
        if (start >= sectionStart && start + signature.Size <= sectionEnd)
        {
            if (foundSignature)
                throw new BadImageFormatException("Signature maps into multiple PE sections.");
            foundSignature = true;
            hash.AppendData(image, sectionStart, start - sectionStart);
            hash.AppendData(image, start + signature.Size, sectionEnd - start - signature.Size);
        }
        else
            hash.AppendData(image, sectionStart, section.SizeOfRawData);
    }
    if (!foundSignature)
        throw new BadImageFormatException("Signature is outside PE section data.");
    var imageHash = hash.GetHashAndReset();
    Array.Reverse(signedBytes); // Strong-name RSA signature bytes are little-endian.
    bool valid;
    try
    {
        valid = rsa.VerifyHash(imageHash, signedBytes,
            hashAlg == 0x8004 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }
    catch (CryptographicException)
    {
        valid = false;
    }
    return (valid ? "verified-signed" : "invalid-signature",
            $"RSA PKCS#1 v1.5; {(hashAlg == 0x8004 ? "SHA-1" : "SHA-256")}; {bits}-bit key; PE digest={Convert.ToHexString(imageHash)}; key SHA-256={Convert.ToHexString(SHA256.HashData(key))}; signature valid={valid}.");
}

static int FileOffset(PEReader pe, int rva, int size)
{
    foreach (var section in pe.PEHeaders.SectionHeaders)
        if (rva >= section.VirtualAddress && size <= section.SizeOfRawData &&
            rva - section.VirtualAddress <= section.SizeOfRawData - size)
            return section.PointerToRawData + rva - section.VirtualAddress;
    throw new BadImageFormatException("Unmapped signature reservation RVA.");
}

static void ValidateCertificates(ReadOnlyMemory<byte> data)
{
    int cursor = 0;
    while (cursor < data.Length)
    {
        var bytes = data.Span;
        if (data.Length - cursor < 8)
            throw new BadImageFormatException("Truncated WIN_CERTIFICATE header.");
        int length = BitConverter.ToInt32(bytes.Slice(cursor, 4));
        if (length < 8 || length > data.Length - cursor ||
            BitConverter.ToUInt16(bytes.Slice(cursor + 4, 2)) != 0x0200 ||
            BitConverter.ToUInt16(bytes.Slice(cursor + 6, 2)) != 0x0002)
            throw new BadImageFormatException("Invalid PKCS#7 WIN_CERTIFICATE record.");
        int alignedLength = (length + 7) & ~7;
        if (alignedLength > data.Length - cursor ||
            data.Span.Slice(cursor + length, alignedLength - length).ContainsAnyExcept((byte)0))
            throw new BadImageFormatException("Uninventoried WIN_CERTIFICATE alignment bytes.");
        var content = data.Slice(cursor + 8, length - 8);
        if (content.Span.IndexOf("MZ"u8) >= 0 ||
            content.Span.IndexOf("PK\x03\x04"u8) >= 0 ||
            content.Span.IndexOf("PK\x01\x02"u8) >= 0 ||
            content.Span.IndexOf("PK\x05\x06"u8) >= 0 ||
            content.Span.IndexOf(new byte[] { 0x1f, 0x8b }) >= 0 ||
            content.Span.IndexOf("MSCF"u8) >= 0 ||
            content.Span.IndexOf(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }) >= 0)
            throw new BadImageFormatException("Potential PE or nested container in certificate data.");
        var reader = new AsnReader(content, AsnEncodingRules.DER);
        ReadOnlyMemory<byte> encoded = reader.ReadEncodedValue();
        ValidateDerTree(encoded);
        var padding = content.Span.Slice(encoded.Length);
        if (padding.Length >= 8 || padding.ContainsAnyExcept((byte)0))
            throw new BadImageFormatException("Uninventoried bytes in Authenticode certificate.");
        var cms = new AsnReader(encoded, AsnEncodingRules.DER);
        var sequence = cms.ReadSequence();
        if (sequence.ReadObjectIdentifier() != "1.2.840.113549.1.7.2")
            throw new BadImageFormatException("Certificate is not PKCS#7 signedData.");
        var signedDataWrapper = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
        var signedData = signedDataWrapper.ReadSequence();
        var version = signedData.ReadInteger();
        if (version != 1 && version != 3)
            throw new BadImageFormatException("Unsupported Authenticode signedData version.");
        var algorithms = signedData.ReadSetOf();
        var encapsulated = signedData.ReadSequence();
        if (encapsulated.ReadObjectIdentifier() != "1.3.6.1.4.1.311.2.1.4")
            throw new BadImageFormatException("Unsupported Authenticode encapsulated content.");
        if (!algorithms.HasData)
            throw new BadImageFormatException("Missing Authenticode digest algorithm.");
        var digestAlgorithms = new System.Collections.Generic.HashSet<string>();
        while (algorithms.HasData)
            digestAlgorithms.Add(ReadDigestAlgorithm(algorithms.ReadSequence()));
        var indirectWrapper = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
        AsnReader indirect;
        if (indirectWrapper.PeekTag().HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.OctetString)))
        {
            var encodedIndirect = new AsnReader(indirectWrapper.ReadOctetString(), AsnEncodingRules.DER);
            indirect = encodedIndirect.ReadSequence();
            encodedIndirect.ThrowIfNotEmpty();
        }
        else
            indirect = indirectWrapper.ReadSequence();
        var attribute = indirect.ReadSequence();
        if (attribute.ReadObjectIdentifier() != "1.3.6.1.4.1.311.2.1.15")
            throw new BadImageFormatException("Unsupported Authenticode subject.");
        if (attribute.HasData)
        {
            var peImage = attribute.ReadSequence();
            _ = peImage.ReadBitString(out _);
            if (peImage.HasData)
            {
                var link = peImage.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                var moniker = link.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2, true));
                _ = moniker.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
                moniker.ThrowIfNotEmpty();
                link.ThrowIfNotEmpty();
            }
            peImage.ThrowIfNotEmpty();
        }
        attribute.ThrowIfNotEmpty();
        var digestInfo = indirect.ReadSequence();
        string digestOid = ReadDigestAlgorithm(digestInfo.ReadSequence());
        if (!digestAlgorithms.Contains(digestOid))
            throw new BadImageFormatException("Authenticode digest algorithm differs from signedData.");
        int digestLength = digestOid == "1.3.14.3.2.26" ? 20 : 32;
        if (digestInfo.ReadOctetString().Length != digestLength)
            throw new BadImageFormatException("Invalid Authenticode digest length.");
        digestInfo.ThrowIfNotEmpty();
        indirect.ThrowIfNotEmpty();
        indirectWrapper.ThrowIfNotEmpty();
        encapsulated.ThrowIfNotEmpty();
        ValidateCertificateSigners(signedData, digestAlgorithms, false);
        signedData.ThrowIfNotEmpty();
        signedDataWrapper.ThrowIfNotEmpty();
        sequence.ThrowIfNotEmpty();
        cms.ThrowIfNotEmpty();
        cursor += alignedLength;
    }
    if (cursor != data.Length)
        throw new BadImageFormatException("Uninventoried certificate table bytes.");
}

static void ValidateCertificateSigners(
    AsnReader signedData, System.Collections.Generic.HashSet<string> digestAlgorithms, bool timestamp)
{
        var certificateIdentities = new System.Collections.Generic.List<(byte[] Issuer, byte[] Serial)>();
        if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
        {
            var certificates = signedData.ReadSetOf(false, new Asn1Tag(TagClass.ContextSpecific, 0, true));
            if (!certificates.HasData)
                throw new BadImageFormatException("Empty Authenticode certificate set.");
            while (certificates.HasData)
            {
                using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(
                    certificates.ReadEncodedValue().Span);
                certificateIdentities.Add((cert.IssuerName.RawData, cert.SerialNumberBytes.ToArray()));
            }
        }
        if (signedData.HasData && signedData.PeekTag().TagClass == TagClass.ContextSpecific)
            throw new BadImageFormatException("Unsupported Authenticode revocation fields.");
        var signers = signedData.ReadSetOf();
        if (!signers.HasData)
            throw new BadImageFormatException("Missing Authenticode signer.");
        while (signers.HasData)
            ValidateSignerInfo(signers.ReadSequence(), digestAlgorithms, certificateIdentities, timestamp);
}

static string ReadDigestAlgorithm(AsnReader algorithm)
{
    string oid = algorithm.ReadObjectIdentifier();
    if (oid is not ("1.3.14.3.2.26" or "2.16.840.1.101.3.4.2.1"))
        throw new BadImageFormatException("Unsupported Authenticode digest algorithm.");
    if (algorithm.HasData)
        algorithm.ReadNull();
    algorithm.ThrowIfNotEmpty();
    return oid;
}

static void ValidateSignerInfo(
    AsnReader signer, System.Collections.Generic.HashSet<string> digestAlgorithms,
    System.Collections.Generic.List<(byte[] Issuer, byte[] Serial)> certificateIdentities,
    bool timestamp)
{
    if (signer.ReadInteger() != 1)
        throw new BadImageFormatException("Unsupported Authenticode signer version.");
    var issuerAndSerial = signer.ReadSequence();
    ReadOnlyMemory<byte> issuer = issuerAndSerial.ReadEncodedValue();
    var issuerName = new AsnReader(issuer, AsnEncodingRules.DER);
    var distinguishedName = issuerName.ReadSequence();
    while (distinguishedName.HasData)
        ValidateDerTree(distinguishedName.ReadEncodedValue());
    issuerName.ThrowIfNotEmpty();
    byte[] serial = issuerAndSerial.ReadIntegerBytes().ToArray();
    while (serial.Length > 1 && serial[0] == 0)
        serial = serial[1..];
    issuerAndSerial.ThrowIfNotEmpty();
    if (!certificateIdentities.Any(certificate =>
        certificate.Issuer.AsSpan().SequenceEqual(issuer.Span) &&
        certificate.Serial.AsSpan().SequenceEqual(serial)))
        throw new BadImageFormatException("Authenticode signer has no matching certificate.");
    if (!digestAlgorithms.Contains(ReadDigestAlgorithm(signer.ReadSequence())))
        throw new BadImageFormatException("Authenticode signer digest algorithm differs from signedData.");
    if (signer.HasData && signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
        ValidateSignerAttributes(signer.ReadSetOf(false, new Asn1Tag(TagClass.ContextSpecific, 0, true)), false, timestamp);
    var signatureAlgorithm = signer.ReadSequence();
    if (signatureAlgorithm.ReadObjectIdentifier() != "1.2.840.113549.1.1.1")
        throw new BadImageFormatException("Unsupported Authenticode signer key algorithm.");
    if (signatureAlgorithm.HasData)
        signatureAlgorithm.ReadNull();
    signatureAlgorithm.ThrowIfNotEmpty();
    if (signer.ReadOctetString().Length == 0)
        throw new BadImageFormatException("Empty Authenticode signer signature.");
    if (signer.HasData && signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
        ValidateSignerAttributes(signer.ReadSetOf(false, new Asn1Tag(TagClass.ContextSpecific, 1, true)), true, timestamp);
    signer.ThrowIfNotEmpty();
}

static void ValidateSignerAttributes(AsnReader attributes, bool unsigned, bool timestamp)
{
    while (attributes.HasData)
    {
        var attribute = attributes.ReadSequence();
        string oid = attribute.ReadObjectIdentifier();
        if ((unsigned && (timestamp || oid != "1.3.6.1.4.1.311.3.3.1")) ||
            (!unsigned && !timestamp && oid is not ("1.3.6.1.4.1.311.2.1.12" or
                                                   "1.2.840.113549.1.9.3" or
                                                   "1.3.6.1.4.1.311.2.1.11" or
                                                   "1.2.840.113549.1.9.4")) ||
            (!unsigned && timestamp && oid is not ("1.2.840.113549.1.9.16.2.15" or
                                                  "1.2.840.113549.1.9.3" or
                                                  "1.2.840.113549.1.9.5" or
                                                  "1.2.840.113549.1.9.4" or
                                                  "1.2.840.113549.1.9.16.2.47" or
                                                  "1.2.840.113549.1.9.16.2.18")))
            throw new BadImageFormatException("Unsupported Authenticode signer attribute.");
        var values = attribute.ReadSetOf();
        if (!values.HasData)
            throw new BadImageFormatException("Empty Authenticode signer attribute.");
        while (values.HasData)
        {
            ReadOnlyMemory<byte> encoded = values.ReadEncodedValue();
            ValidateDerTree(encoded);
            var value = new AsnReader(encoded, AsnEncodingRules.DER);
            if (oid == "1.2.840.113549.1.9.3")
            {
                string expected = timestamp ? "1.2.840.113549.1.9.16.1.4" :
                    "1.3.6.1.4.1.311.2.1.4";
                if (value.ReadObjectIdentifier() != expected)
                    throw new BadImageFormatException("Signer content type differs from Authenticode profile.");
            }
            else if (oid == "1.2.840.113549.1.9.4")
            {
                if (value.ReadOctetString().Length is not (20 or 32))
                    throw new BadImageFormatException("Signer message digest has an unsupported length.");
            }
            else if (oid == "1.3.6.1.4.1.311.2.1.11")
            {
                var statement = value.ReadSequence();
                if (statement.ReadObjectIdentifier() != "1.3.6.1.4.1.311.2.1.21")
                    throw new BadImageFormatException("Unsupported Authenticode statement.");
                statement.ThrowIfNotEmpty();
            }
            else if (oid == "1.2.840.113549.1.9.5")
                _ = value.ReadUtcTime();
            else if (oid == "1.3.6.1.4.1.311.2.1.12")
            {
                var opus = value.ReadSequence();
                for (int field = 0; field <= 1; field++)
                    if (opus.HasData && opus.PeekTag().HasSameClassAndValue(
                        new Asn1Tag(TagClass.ContextSpecific, field)))
                    {
                        var text = opus.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, field, true));
                        _ = text.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
                        text.ThrowIfNotEmpty();
                    }
                opus.ThrowIfNotEmpty();
            }
            else if (oid == "1.2.840.113549.1.9.16.2.15")
                value.ReadNull();
            else if (oid == "1.2.840.113549.1.9.16.2.47")
            {
                var signingCertificate = value.ReadSequence();
                var certs = signingCertificate.ReadSequence();
                if (!certs.HasData)
                    throw new BadImageFormatException("Empty timestamp signing certificate list.");
                while (certs.HasData)
                {
                    var cert = certs.ReadSequence();
                    string algorithm = cert.PeekTag().HasSameClassAndValue(
                        new Asn1Tag(UniversalTagNumber.Sequence)) ?
                        ReadDigestAlgorithm(cert.ReadSequence()) : "2.16.840.1.101.3.4.2.1";
                    if (cert.ReadOctetString().Length != (algorithm == "1.3.14.3.2.26" ? 20 : 32))
                        throw new BadImageFormatException("Invalid timestamp certificate digest.");
                    if (cert.HasData)
                    {
                        var issuerSerial = cert.ReadSequence();
                        var issuers = issuerSerial.ReadSequence();
                        if (!issuers.HasData)
                            throw new BadImageFormatException("Empty timestamp certificate issuer.");
                        while (issuers.HasData)
                        {
                            var directoryName = issuers.ReadSequence(
                                new Asn1Tag(TagClass.ContextSpecific, 4, true));
                            _ = directoryName.ReadSequence();
                            directoryName.ThrowIfNotEmpty();
                        }
                        _ = issuerSerial.ReadInteger();
                        issuerSerial.ThrowIfNotEmpty();
                    }
                    cert.ThrowIfNotEmpty();
                }
                signingCertificate.ThrowIfNotEmpty();
            }
            else if (oid == "1.2.840.113549.1.9.16.2.18")
            {
                var certificateValues = value.ReadSequence();
                if (!certificateValues.HasData)
                    throw new BadImageFormatException("Empty timestamp certificate values.");
                while (certificateValues.HasData)
                {
                    var certificate = certificateValues.ReadSequence(
                        new Asn1Tag(TagClass.ContextSpecific, 1, true));
                    var description = certificate.ReadSequence();
                    var fields = description.ReadSequence();
                    while (fields.HasData)
                        ValidateDerTree(fields.ReadEncodedValue());
                    var signature = description.ReadSequence();
                    if (signature.ReadObjectIdentifier() != "1.2.840.113549.1.1.11")
                        throw new BadImageFormatException("Unsupported timestamp certificate-value signature.");
                    if (signature.HasData)
                        signature.ReadNull();
                    signature.ThrowIfNotEmpty();
                    if (description.ReadBitString(out _).Length == 0)
                        throw new BadImageFormatException("Missing timestamp certificate-value signature.");
                    description.ThrowIfNotEmpty();
                    certificate.ThrowIfNotEmpty();
                }
            }
            else if (oid == "1.3.6.1.4.1.311.3.3.1")
            {
                var timestampContentInfo = value.ReadSequence();
                if (timestampContentInfo.ReadObjectIdentifier() != "1.2.840.113549.1.7.2")
                    throw new BadImageFormatException("Unsupported Authenticode timestamp content.");
                var timestampWrapper = timestampContentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                var timestampSignedData = timestampWrapper.ReadSequence();
                ValidateTimestampSignedData(timestampSignedData);
                timestampSignedData.ThrowIfNotEmpty();
                timestampWrapper.ThrowIfNotEmpty();
                timestampContentInfo.ThrowIfNotEmpty();
            }
            else
                throw new BadImageFormatException("Unsupported Authenticode signer attribute value.");
            value.ThrowIfNotEmpty();
        }
        attribute.ThrowIfNotEmpty();
    }
}

static void ValidateTimestampSignedData(AsnReader signedData)
{
    if (signedData.ReadInteger() != 3)
        throw new BadImageFormatException("Unsupported Authenticode timestamp version.");
    var algorithms = signedData.ReadSetOf();
    var digestAlgorithms = new System.Collections.Generic.HashSet<string>();
    while (algorithms.HasData)
        digestAlgorithms.Add(ReadDigestAlgorithm(algorithms.ReadSequence()));
    if (digestAlgorithms.Count == 0)
        throw new BadImageFormatException("Missing Authenticode timestamp digest.");
    var encapsulated = signedData.ReadSequence();
    if (encapsulated.ReadObjectIdentifier() != "1.2.840.113549.1.9.16.1.4")
        throw new BadImageFormatException("Unsupported Authenticode timestamp eContent.");
    var wrapper = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
    var tstReader = new AsnReader(wrapper.ReadOctetString(), AsnEncodingRules.DER);
    var tst = tstReader.ReadSequence();
    if (tst.ReadInteger() != 1)
        throw new BadImageFormatException("Unsupported Authenticode timestamp token version.");
    _ = tst.ReadObjectIdentifier();
    var imprint = tst.ReadSequence();
    string digestOid = ReadDigestAlgorithm(imprint.ReadSequence());
    if (!digestAlgorithms.Contains(digestOid) ||
        imprint.ReadOctetString().Length != (digestOid == "1.3.14.3.2.26" ? 20 : 32))
        throw new BadImageFormatException("Invalid Authenticode timestamp imprint.");
    imprint.ThrowIfNotEmpty();
    _ = tst.ReadInteger();
    _ = tst.ReadGeneralizedTime();
    int lastOptional = 0;
    while (tst.HasData)
    {
        var tag = tst.PeekTag();
        int optional = tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.Sequence)) ? 1 :
            tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.Boolean)) ? 2 :
            tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.Integer)) ? 3 :
            tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 0 ? 4 :
            tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 1 ? 5 : 0;
        if (optional == 0 || optional <= lastOptional)
            throw new BadImageFormatException("Unsupported Authenticode timestamp field.");
        lastOptional = optional;
        ValidateDerTree(tst.ReadEncodedValue());
    }
    tstReader.ThrowIfNotEmpty();
    wrapper.ThrowIfNotEmpty();
    encapsulated.ThrowIfNotEmpty();
    ValidateCertificateSigners(signedData, digestAlgorithms, true);
}

static void ValidateDerTree(ReadOnlyMemory<byte> encoded, int depth = 0)
{
    if (depth > 32)
        throw new BadImageFormatException("Authenticode certificate DER nesting is too deep.");
    var tag = AsnDecoder.ReadEncodedValue(encoded.Span, AsnEncodingRules.DER,
        out int offset, out int length, out int consumed);
    if (consumed != encoded.Length)
        throw new BadImageFormatException("Uninventoried bytes in certificate DER field.");
    if (tag.TagClass == TagClass.Universal)
    {
        bool constructed = tag.TagValue is 16 or 17;
        bool primitive = tag.TagValue is 1 or 2 or 3 or 4 or 5 or 6 or 10 or
            12 or 18 or 19 or 20 or 22 or 23 or 24 or 26 or 27 or 28 or 30;
        if (!(constructed && tag.IsConstructed || primitive && !tag.IsConstructed))
            throw new BadImageFormatException("Noncanonical or unsupported universal DER tag.");
    }
    if (!tag.IsConstructed)
        return;
    int cursor = offset;
    while (cursor < offset + length)
    {
        _ = AsnDecoder.ReadEncodedValue(encoded.Span[cursor..], AsnEncodingRules.DER,
            out _, out _, out int fieldLength);
        ValidateDerTree(encoded.Slice(cursor, fieldLength), depth + 1);
        cursor += fieldLength;
    }
}

static void ValidateMetadataTables(PEReader pe, MetadataReader metadata)
{
    byte[] content = pe.GetMetadata().GetContent().ToArray();
    if (content.Length < 24 || BitConverter.ToUInt32(content, 0) != 0x424a5342)
        throw new BadImageFormatException("Metadata root is invalid.");
    int versionLength = BitConverter.ToInt32(content, 12);
    if (versionLength < 0 || versionLength > content.Length - 20)
        throw new BadImageFormatException("Metadata version header is invalid.");
    int cursor = 16 + versionLength;
    int streams = BitConverter.ToUInt16(content, cursor + 2);
    if (streams is < 1 or > 64)
        throw new BadImageFormatException("Metadata stream count is invalid.");
    cursor += 4;
    int tableHeader = -1;
    int tableSize = 0;
    for (int stream = 0; stream < streams; stream++)
    {
        if (cursor > content.Length - 9)
            throw new BadImageFormatException("Metadata stream header is truncated.");
        int offset = BitConverter.ToInt32(content, cursor);
        int size = BitConverter.ToInt32(content, cursor + 4);
        int end = Array.IndexOf(content, (byte)0, cursor + 8,
            Math.Min(40, content.Length - cursor - 8));
        if (end < 0 || offset < 0 || size < 0 || offset > content.Length - size)
            throw new BadImageFormatException("Metadata stream is invalid.");
        string name = System.Text.Encoding.ASCII.GetString(content, cursor + 8, end - cursor - 8);
        if (name is "#~" or "#-")
        {
            if (tableHeader >= 0 || size < 24)
                throw new BadImageFormatException("Metadata tables are ambiguous or truncated.");
            tableHeader = offset;
            tableSize = size;
        }
        cursor = (end + 4) & ~3;
    }
    if (tableHeader < 0)
        throw new BadImageFormatException("Metadata tables are missing.");
    ulong mask = BitConverter.ToUInt64(content, tableHeader + 8);
    if ((mask & 1) == 0 || (mask >> 45) != 0)
        throw new BadImageFormatException("Unsupported or absent metadata table shape.");

    int strings = metadata.GetHeapSize(HeapIndex.String);
    int blobs = metadata.GetHeapSize(HeapIndex.Blob);
    int guids = metadata.GetHeapSize(HeapIndex.Guid);
    var typeProvider = new MetadataTypeProvider(metadata);
    for (int table = 0; table <= 44; table++)
    {
        int count = metadata.GetTableRowCount((TableIndex)table);
        if (count == 0)
            continue;
        string[] fields = MetadataSchema(table).Split(' ');
        int size = metadata.GetTableRowSize((TableIndex)table);
        int offset = metadata.GetTableMetadataOffset((TableIndex)table);
        if (size <= 0 || offset < tableHeader ||
            (long)offset + (long)count * size > tableHeader + (long)tableSize)
            throw new BadImageFormatException($"Metadata table {table} exceeds its stream.");
        int[] lastList = new int[fields.Length];
        for (int row = 0; row < count; row++)
        {
            int position = offset + row * size;
            for (int column = 0; column < fields.Length; column++)
            {
                string field = fields[column];
                int width = field switch
                {
                    "u2" => 2,
                    "u4" => 4,
                    "str" => strings > ushort.MaxValue ? 4 : 2,
                    "blob" => blobs > ushort.MaxValue ? 4 : 2,
                    "guid" => guids > ushort.MaxValue ? 4 : 2,
                    _ when field.StartsWith("t:") || field.StartsWith("l:") =>
                        metadata.GetTableRowCount((TableIndex)int.Parse(field.AsSpan(2))) > ushort.MaxValue ? 4 : 2,
                    _ when field.StartsWith("c:") => CodedWidth(metadata, field[2..]),
                    _ => throw new BadImageFormatException($"Unsupported metadata field {field}."),
                };
                if (position + width > offset + (row + 1) * size)
                    throw new BadImageFormatException($"Truncated row in metadata table {table}.");
                int value = width == 2 ? BitConverter.ToUInt16(content, position)
                                       : BitConverter.ToInt32(content, position);
                if (value < 0)
                    throw new BadImageFormatException($"Negative metadata index in table {table}.");
                switch (field)
                {
                    case "str":
                        bool requiredName = (table, column) is (0, 1) or (1, 1) or (2, 1) or
                            (4, 1) or (6, 3) or (10, 1) or (20, 1) or (23, 1) or
                            (32, 7) or (35, 6) or (40, 2) or (42, 3);
                        if (value == 0 && requiredName)
                            throw new BadImageFormatException($"Missing metadata name in table {table}, row {row + 1}.");
                        if (value != 0)
                        {
                            if (value >= strings)
                                throw new BadImageFormatException($"Out-of-range #Strings index in table {table}, row {row + 1}.");
                            string text = metadata.GetString(MetadataTokens.StringHandle(value));
                            if (requiredName && string.IsNullOrWhiteSpace(text))
                                throw new BadImageFormatException($"Empty metadata name in table {table}, row {row + 1}.");
                        }
                        break;
                    case "blob":
                        bool requiredBlob = (table, column) is (4, 2) or (6, 4) or (10, 2) or
                            (14, 2) or (17, 0) or (23, 2) or (27, 0) or (43, 1);
                        // Null string/class constants have no Value blob; primitive constants do.
                        if (table == 11 && column == 2 &&
                            content[offset + row * size] is not (0x0e or 0x12))
                            requiredBlob = true;
                        if (table == 35 && column == 5 &&
                            (BitConverter.ToUInt32(content, offset + row * size + 8) & 1) != 0)
                            requiredBlob = true;
                        if (value == 0 && requiredBlob)
                            throw new BadImageFormatException($"Missing metadata blob in table {table}, row {row + 1}.");
                        if (value != 0)
                        {
                            if (value >= blobs)
                                throw new BadImageFormatException($"Out-of-range #Blob index in table {table}, row {row + 1}.");
                            byte[] blob = metadata.GetBlobBytes(MetadataTokens.BlobHandle(value));
                            if (blob.Length == 0 &&
                                ((table, column) is (4, 2) or (6, 4) or (10, 2) or
                                    (12, 2) or (14, 2) or (17, 0) or (23, 2) or
                                    (27, 0) or (35, 5) or (43, 1) ||
                                 (table == 11 && content[offset + row * size] is not (0x0e or 0x12))))
                                throw new BadImageFormatException($"Empty metadata blob in table {table}, row {row + 1}.");
                            if ((table, column) == (12, 2) &&
                                (blob.Length < 4 || blob[0] != 1 || blob[1] != 0))
                                throw new BadImageFormatException($"Malformed CustomAttribute.Value in row {row + 1}.");
                            if (column == 2 && table == 12)
                                ValidateAttribute(metadata, typeProvider, row + 1);
                            else if ((table, column) is (4, 2) or (6, 4) or (10, 2) or
                                     (17, 0) or (23, 2) or (27, 0) or (43, 1))
                                ValidateSignature(metadata, typeProvider, table,
                                    MetadataTokens.BlobHandle(value));
                        }
                        break;
                    case "guid":
                        if (value == 0 && (table, column) == (0, 2))
                            throw new BadImageFormatException($"Missing module MVID in row {row + 1}.");
                        if (value != 0)
                        {
                            if (value > guids / 16)
                                throw new BadImageFormatException($"Out-of-range #GUID index in table {table}, row {row + 1}.");
                            _ = metadata.GetGuid(MetadataTokens.GuidHandle(value));
                        }
                        break;
                    default:
                        if (field.StartsWith("t:") || field.StartsWith("l:"))
                        {
                            int target = int.Parse(field.AsSpan(2));
                            int maximum = metadata.GetTableRowCount((TableIndex)target) +
                                (field[0] == 'l' ? 1 : 0);
                            if (value == 0 || value > maximum)
                                throw new BadImageFormatException($"Out-of-range table reference in table {table}, row {row + 1}.");
                            if (field[0] == 'l')
                            {
                                if (value < lastList[column])
                                    throw new BadImageFormatException($"Unordered list reference in table {table}, row {row + 1}.");
                                lastList[column] = value;
                            }
                        }
                        else if (field.StartsWith("c:"))
                        {
                            bool requiredReference = (table, column) is (9, 1) or (10, 0) or
                                (11, 1) or (12, 0) or (12, 1) or (14, 1) or (20, 2) or
                                (24, 2) or (25, 1) or (25, 2) or (42, 2) or (43, 0) or
                                (44, 1);
                            if (value == 0 && requiredReference)
                                throw new BadImageFormatException($"Missing coded reference in table {table}, row {row + 1}.");
                            if (value == 0)
                            {
                                position += width;
                                continue;
                            }
                            var (bits, targets) = CodedIndex(field[2..]);
                            int tag = value & ((1 << bits) - 1), reference = value >> bits;
                            if (tag >= targets.Length || targets[tag] < 0 || reference == 0 ||
                                reference > metadata.GetTableRowCount((TableIndex)targets[tag]))
                                throw new BadImageFormatException($"Out-of-range coded reference in table {table}, row {row + 1}.");
                        }
                        break;
                }
                position += width;
            }
            if (position != offset + (row + 1) * size)
                throw new BadImageFormatException($"Unsupported trailing metadata columns in table {table}.");
        }
    }
}

static int CodedWidth(MetadataReader metadata, string name)
{
    var (bits, targets) = CodedIndex(name);
    int maximum = targets.Where(target => target >= 0).Max(target =>
        metadata.GetTableRowCount((TableIndex)target));
    return maximum < 1 << (16 - bits) ? 2 : 4;
}

static (int bits, int[] targets) CodedIndex(string name) => name switch
{
    "TypeDefOrRef" => (2, [2, 1, 27]),
    "ResolutionScope" => (2, [0, 26, 35, 1]),
    "HasConstant" => (2, [4, 8, 23]),
    "HasCustomAttribute" => (5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17,
                                  26, 27, 32, 35, 38, 39, 40, 42, 44, 43]),
    "CustomAttributeType" => (3, [-1, -1, 6, 10]),
    "HasDeclSecurity" => (2, [2, 6, 32]),
    "MemberRefParent" => (3, [2, 1, 26, 6, 27]),
    "HasSemantics" => (1, [20, 23]),
    "MethodDefOrRef" => (1, [6, 10]),
    "TypeOrMethodDef" => (1, [2, 6]),
    "Implementation" => (2, [38, 35, 39]),
    _ => throw new BadImageFormatException($"Unsupported coded metadata reference: {name}."),
};

static string MetadataSchema(int table) => table switch
{
    0 => "u2 str guid guid guid",
    1 => "c:ResolutionScope str str",
    2 => "u4 str str c:TypeDefOrRef l:4 l:6",
    4 => "u2 str blob",
    6 => "u4 u2 u2 str blob l:8",
    8 => "u2 u2 str",
    9 => "t:2 c:TypeDefOrRef",
    10 => "c:MemberRefParent str blob",
    11 => "u2 c:HasConstant blob",
    12 => "c:HasCustomAttribute c:CustomAttributeType blob",
    14 => "u2 c:HasDeclSecurity blob",
    15 => "u2 u4 t:2",
    16 => "u4 t:4",
    17 => "blob",
    18 => "t:2 l:20",
    20 => "u2 str c:TypeDefOrRef",
    21 => "t:2 l:23",
    23 => "u2 str blob",
    24 => "u2 t:6 c:HasSemantics",
    25 => "t:2 c:MethodDefOrRef c:MethodDefOrRef",
    27 => "blob",
    29 => "u4 t:4",
    32 => "u4 u2 u2 u2 u2 u4 blob str str",
    35 => "u2 u2 u2 u2 u4 blob str str blob",
    40 => "u4 u4 str c:Implementation",
    41 => "t:2 t:2",
    42 => "u2 u2 c:TypeOrMethodDef str",
    43 => "c:MethodDefOrRef blob",
    44 => "t:42 c:TypeDefOrRef",
    _ => throw new BadImageFormatException($"Unsupported metadata table {table}."),
};

static void ValidateSignature(MetadataReader metadata, MetadataTypeProvider provider,
    int table, BlobHandle handle)
{
    var reader = metadata.GetBlobReader(handle);
    if (reader.Length > 1024 * 1024)
        throw new BadImageFormatException("Metadata signature exceeds supported size.");
    var decoder = new SignatureDecoder<string, object?>(provider, metadata, null);
    if (table is 4 or 6 or 10 or 17 or 23 or 43)
    {
        var headerReader = reader;
        var kind = headerReader.ReadSignatureHeader().Kind;
        switch (table, kind)
        {
            case (4, SignatureKind.Field):
            case (10, SignatureKind.Field):
                _ = decoder.DecodeFieldSignature(ref reader);
                break;
            case (6, SignatureKind.Method):
            case (10, SignatureKind.Method):
            case (17, SignatureKind.Method):
            case (23, SignatureKind.Property):
                _ = decoder.DecodeMethodSignature(ref reader);
                break;
            case (17, SignatureKind.LocalVariables):
                _ = decoder.DecodeLocalSignature(ref reader);
                break;
            case (43, SignatureKind.MethodSpecification):
                _ = decoder.DecodeMethodSpecificationSignature(ref reader);
                break;
            default:
                throw new BadImageFormatException($"Unsupported signature kind {kind} in table {table}.");
        }
    }
    else if (table == 27)
        _ = decoder.DecodeType(ref reader);
    if (reader.RemainingBytes != 0)
        throw new BadImageFormatException($"Trailing signature bytes in table {table}.");
}

static void ValidateAttribute(MetadataReader metadata, MetadataTypeProvider provider, int row)
{
    var attribute = metadata.GetCustomAttribute(MetadataTokens.CustomAttributeHandle(row));
    if (attribute.Value.IsNil)
        return;
    BlobHandle ctor = attribute.Constructor.Kind switch
    {
        HandleKind.MethodDefinition =>
            metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).Signature,
        HandleKind.MemberReference =>
            metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Signature,
        _ => throw new BadImageFormatException("Unsupported custom attribute constructor."),
    };
    string constructorName = attribute.Constructor.Kind == HandleKind.MethodDefinition
        ? metadata.GetString(metadata.GetMethodDefinition(
            (MethodDefinitionHandle)attribute.Constructor).Name)
        : metadata.GetString(metadata.GetMemberReference(
            (MemberReferenceHandle)attribute.Constructor).Name);
    if (constructorName != ".ctor")
        throw new BadImageFormatException($"Non-constructor custom attribute in row {row}.");
    var signatureReader = metadata.GetBlobReader(ctor);
    var decoder = new SignatureDecoder<string, object?>(provider, metadata, null);
    var signature = decoder.DecodeMethodSignature(ref signatureReader);
    if (signatureReader.RemainingBytes != 0 || signature.Header.Kind != SignatureKind.Method ||
        signature.Header.IsGeneric || !signature.Header.IsInstance ||
        signature.ReturnType != "Void")
        throw new BadImageFormatException($"Invalid custom attribute constructor signature in row {row}.");
    var value = metadata.GetBlobReader(attribute.Value);
    if (value.Length > 1024 * 1024 || value.ReadUInt16() != 1)
        throw new BadImageFormatException($"Invalid custom attribute prolog in row {row}.");
    foreach (string type in signature.ParameterTypes)
        ReadAttributeValue(ref value, provider, type, 0);
    int named = value.ReadUInt16();
    for (int i = 0; i < named; i++)
    {
        byte kind = value.ReadByte();
        if (kind is not (0x53 or 0x54))
            throw new BadImageFormatException($"Invalid custom attribute named argument kind in row {row}.");
        string type = ReadAttributeFieldType(ref value, provider, 0);
        string? name = value.ReadSerializedString();
        if (string.IsNullOrEmpty(name))
            throw new BadImageFormatException($"Invalid custom attribute named argument name in row {row}.");
        ReadAttributeValue(ref value, provider, type, 0);
    }
    if (value.RemainingBytes != 0)
        throw new BadImageFormatException($"Trailing custom attribute data in row {row}.");
    _ = attribute.DecodeValue(provider);
}

static string ReadAttributeFieldType(ref BlobReader reader, MetadataTypeProvider provider, int depth)
{
    if (depth > 32)
        throw new BadImageFormatException("Custom attribute type nesting exceeds supported depth.");
    var code = reader.ReadSerializationTypeCode();
    return code switch
    {
        SerializationTypeCode.Type => "System.Type",
        SerializationTypeCode.TaggedObject => "Object",
        SerializationTypeCode.Enum => provider.GetTypeFromSerializedName(
            reader.ReadSerializedString() ?? throw new BadImageFormatException("Null enum name.")),
        SerializationTypeCode.SZArray => ReadAttributeFieldType(ref reader, provider, depth + 1) + "[]",
        >= SerializationTypeCode.Boolean and <= SerializationTypeCode.String =>
            provider.GetPrimitiveType((PrimitiveTypeCode)code),
        _ => throw new BadImageFormatException($"Unsupported custom attribute type {code}."),
    };
}

static void ReadAttributeValue(ref BlobReader reader, MetadataTypeProvider provider,
    string type, int depth)
{
    if (depth > 32)
        throw new BadImageFormatException("Custom attribute value nesting exceeds supported depth.");
    if (type.EndsWith("[]", StringComparison.Ordinal))
    {
        int count = reader.ReadInt32();
        if (count < -1 || count > reader.RemainingBytes + 1)
            throw new BadImageFormatException("Custom attribute array count exceeds available bytes.");
        for (int i = 0; i < count; i++)
            ReadAttributeValue(ref reader, provider, type[..^2], depth + 1);
        return;
    }
    if (type is "String" or "System.Type")
    {
        _ = reader.ReadSerializedString();
        return;
    }
    if (type == "Object")
    {
        ReadAttributeValue(ref reader, provider, ReadAttributeFieldType(ref reader, provider, depth + 1),
            depth + 1);
        return;
    }
    var primitive = Enum.TryParse<PrimitiveTypeCode>(type, out var code) ? code
        : provider.GetUnderlyingEnumType(type);
    switch (primitive)
    {
        case PrimitiveTypeCode.Boolean:
            if (reader.ReadByte() > 1)
                throw new BadImageFormatException("Noncanonical custom attribute boolean.");
            break;
        case PrimitiveTypeCode.Char: _ = reader.ReadChar(); break;
        case PrimitiveTypeCode.SByte: _ = reader.ReadSByte(); break;
        case PrimitiveTypeCode.Byte: _ = reader.ReadByte(); break;
        case PrimitiveTypeCode.Int16: _ = reader.ReadInt16(); break;
        case PrimitiveTypeCode.UInt16: _ = reader.ReadUInt16(); break;
        case PrimitiveTypeCode.Int32: _ = reader.ReadInt32(); break;
        case PrimitiveTypeCode.UInt32: _ = reader.ReadUInt32(); break;
        case PrimitiveTypeCode.Int64: _ = reader.ReadInt64(); break;
        case PrimitiveTypeCode.UInt64: _ = reader.ReadUInt64(); break;
        case PrimitiveTypeCode.Single: _ = reader.ReadSingle(); break;
        case PrimitiveTypeCode.Double: _ = reader.ReadDouble(); break;
        default: throw new BadImageFormatException($"Unsupported custom attribute value type {type}.");
    }
}

sealed class MetadataTypeProvider(MetadataReader metadata) :
    ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
{
    private readonly HashSet<int> activeTypeSpecs = [];
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
    public string GetSystemType() => "System.Type";
    public bool IsSystemType(string type) => type == "System.Type";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => GetDefinitionName(reader, handle, 0);
    private string GetDefinitionName(MetadataReader reader, TypeDefinitionHandle handle, int depth)
    {
        if (depth > 32)
            throw new BadImageFormatException("Nested TypeDef chain exceeds supported depth.");
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        if (!parent.IsNil)
            return GetDefinitionName(reader, parent, depth + 1) + "+" + reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        return (ns.Length == 0 ? "" : ns + ".") + reader.GetString(type.Name);
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        => GetReferenceName(reader, handle, 0);
    private string GetReferenceName(MetadataReader reader, TypeReferenceHandle handle, int depth)
    {
        if (depth > 32)
            throw new BadImageFormatException("Nested TypeRef chain exceeds supported depth.");
        var type = reader.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope, depth + 1)
                + "+" + reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        return (ns.Length == 0 ? "" : ns + ".") + reader.GetString(type.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? context,
        TypeSpecificationHandle handle, byte rawTypeKind)
    {
        int index = MetadataTokens.GetRowNumber(handle);
        if (activeTypeSpecs.Count >= 32 || !activeTypeSpecs.Add(index))
            throw new BadImageFormatException("Recursive TypeSpec signature.");
        try
        {
            var signature = reader.GetTypeSpecification(handle).Signature;
            var blob = reader.GetBlobReader(signature);
            var decoder = new SignatureDecoder<string, object?>(this, reader, context);
            string result = decoder.DecodeType(ref blob);
            if (blob.RemainingBytes != 0)
                throw new BadImageFormatException("Trailing TypeSpec signature bytes.");
            return result;
        }
        finally
        {
            activeTypeSpecs.Remove(index);
        }
    }
    public string GetTypeFromSerializedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BadImageFormatException("Empty serialized type name.");
        return name;
    }
    public bool IsEnum(string type) => type is not ("System.Type" or "String" or "Object")
        && !Enum.TryParse<PrimitiveTypeCode>(type, out _);
    public PrimitiveTypeCode GetUnderlyingEnumType(string type)
    {
        string name = type;
        bool qualified = type.Contains(',', StringComparison.Ordinal);
        AssemblyName? specifiedAssembly = null;
        if (qualified)
        {
            int separator = type.IndexOf(',');
            name = type[..separator];
            string identity = type[(separator + 1)..].Trim();
            try
            {
                specifiedAssembly = new AssemblyName(identity);
            }
            catch (Exception error) when (error is ArgumentException or FileLoadException)
            {
                throw new BadImageFormatException("Invalid serialized enum assembly identity.", error);
            }
            if (identity != specifiedAssembly.FullName ||
                !IsSupportedEnumAssembly(name, specifiedAssembly) ||
                !HasSupportedEnumReference(name, specifiedAssembly))
                throw new BadImageFormatException($"Unsupported serialized enum assembly identity: {identity}.");
        }
        foreach (var handle in metadata.TypeDefinitions)
        {
            var definition = metadata.GetTypeDefinition(handle);
            if (qualified || GetTypeFromDefinition(metadata, handle, 0) != name)
                continue;
            foreach (var fieldHandle in definition.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if (metadata.GetString(field.Name) != "value__")
                    continue;
                var reader = metadata.GetBlobReader(field.Signature);
                if (reader.ReadSignatureHeader().Kind != SignatureKind.Field)
                    break;
                var code = (PrimitiveTypeCode)reader.ReadSignatureTypeCode();
                if (reader.RemainingBytes == 0 && code is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Byte
                    or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int32
                    or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64)
                    return code;
            }
        }
        if (name.StartsWith("System.", StringComparison.Ordinal) &&
            (qualified || HasSupportedEnumReference(name)))
        {
            Type? known = typeof(object).Assembly.GetType(name)
                ?? typeof(System.ComponentModel.EditorBrowsableState).Assembly.GetType(name)
                ?? typeof(System.Text.RegularExpressions.RegexOptions).Assembly.GetType(name)
                ?? typeof(System.Text.Json.JsonSerializerOptions).Assembly.GetType(name);
            if (known?.IsEnum == true)
            {
                if (specifiedAssembly is not null &&
                    known.Assembly.GetName().Name != specifiedAssembly.Name &&
                    !(known.Assembly == typeof(object).Assembly &&
                      specifiedAssembly.Name == "System.Runtime"))
                    throw new BadImageFormatException($"Serialized enum assembly does not define {name}.");
                return Type.GetTypeCode(Enum.GetUnderlyingType(known)) switch
                {
                    TypeCode.SByte => PrimitiveTypeCode.SByte,
                    TypeCode.Byte => PrimitiveTypeCode.Byte,
                    TypeCode.Int16 => PrimitiveTypeCode.Int16,
                    TypeCode.UInt16 => PrimitiveTypeCode.UInt16,
                    TypeCode.Int32 => PrimitiveTypeCode.Int32,
                    TypeCode.UInt32 => PrimitiveTypeCode.UInt32,
                    TypeCode.Int64 => PrimitiveTypeCode.Int64,
                    TypeCode.UInt64 => PrimitiveTypeCode.UInt64,
                    _ => throw new BadImageFormatException($"Unsupported enum backing type {type}."),
                };
            }
        }
        throw new BadImageFormatException($"Unsupported external enum type {type}.");
    }

    private bool IsSupportedEnumAssembly(string name, AssemblyName identity)
    {
        string? token = identity.GetPublicKeyToken() is { Length: > 0 } key
            ? Convert.ToHexString(key).ToLowerInvariant() : null;
        if (!name.StartsWith("System.", StringComparison.Ordinal) ||
            identity.Version is null ||
            identity.Version.Major is < 8 or > 10 || identity.Version.Minor != 0 ||
            identity.Version.Build != 0 || identity.Version.Revision != 0 ||
            identity.CultureName is not (null or "" or "neutral") ||
            (identity.Name, token) is not
                (("System.Runtime" or "System.ComponentModel.Primitives" or
                  "System.Text.RegularExpressions"), "b03f5f7f11d50a3a") and not
                ("System.Text.Json", "cc7b13ffcd2ddd51") and not
                ("System.Private.CoreLib", "7cec85d7bea7798e"))
            return false;
        return metadata.AssemblyReferences.Any(handle =>
        {
            var reference = metadata.GetAssemblyReference(handle);
            return metadata.GetString(reference.Name) == identity.Name &&
                reference.Version == identity.Version &&
                metadata.GetString(reference.Culture) == (identity.CultureName == "neutral"
                    ? "" : identity.CultureName ?? "") &&
                Convert.ToHexString(metadata.GetBlobBytes(reference.PublicKeyOrToken))
                    .Equals(token, StringComparison.OrdinalIgnoreCase);
        });
    }

    private bool HasSupportedEnumReference(string name, AssemblyName? specifiedAssembly = null)
    {
        foreach (var handle in metadata.TypeReferences)
        {
            if (GetTypeFromReference(metadata, handle, 0) != name)
                continue;
            var reference = metadata.GetTypeReference(handle);
            int depth = 0;
            while (reference.ResolutionScope.Kind == HandleKind.TypeReference && depth++ < 32)
                reference = metadata.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope);
            if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;
            var assembly = metadata.GetAssemblyReference(
                (AssemblyReferenceHandle)reference.ResolutionScope);
            var identity = new AssemblyName(metadata.GetString(assembly.Name))
            {
                Version = assembly.Version,
                CultureName = metadata.GetString(assembly.Culture),
            };
            identity.SetPublicKeyToken(metadata.GetBlobBytes(assembly.PublicKeyOrToken));
            if (IsSupportedEnumAssembly(name, identity) &&
                (specifiedAssembly is null || identity.FullName == specifiedAssembly.FullName))
                return true;
        }
        return false;
    }
}
}
