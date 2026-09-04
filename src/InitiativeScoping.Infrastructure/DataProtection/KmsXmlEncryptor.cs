using System.Text;
using System.Xml.Linq;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace InitiativeScoping.Infrastructure.DataProtection;

/// <summary>
/// Wraps Data Protection key-ring XML with a Cloud KMS symmetric key so keys are not stored in
/// plaintext in the database. Each key element is encrypted directly (they are well under the
/// 64 KiB KMS plaintext limit); the ciphertext carries the KMS key version, so key rotation in
/// KMS is transparent as long as old versions stay enabled.
/// </summary>
public sealed class KmsXmlEncryptor(KeyManagementServiceClient kms, CryptoKeyName keyName) : IXmlEncryptor
{
    internal const string ElementName = "kmsEncryptedKey";
    internal const string KeyAttribute = "keyName";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var response = kms.Encrypt(keyName, ByteString.CopyFrom(plaintext));

        var element = new XElement(ElementName,
            new XAttribute(KeyAttribute, keyName.ToString()),
            new XElement("value", Convert.ToBase64String(response.Ciphertext.ToByteArray())));

        return new EncryptedXmlInfo(element, typeof(KmsXmlDecryptor));
    }
}

public sealed class KmsXmlDecryptor(KeyManagementServiceClient kms) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var keyName = CryptoKeyName.Parse(encryptedElement.Attribute(KmsXmlEncryptor.KeyAttribute)?.Value
            ?? throw new InvalidOperationException("Encrypted key element is missing the KMS key name."));
        var ciphertext = Convert.FromBase64String(encryptedElement.Element("value")?.Value
            ?? throw new InvalidOperationException("Encrypted key element is missing its value."));

        var response = kms.Decrypt(keyName, ByteString.CopyFrom(ciphertext));
        return XElement.Parse(Encoding.UTF8.GetString(response.Plaintext.ToByteArray()));
    }
}
