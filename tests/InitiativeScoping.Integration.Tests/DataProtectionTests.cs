using System.Xml.Linq;
using Google.Api.Gax.Grpc;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using InitiativeScoping.Infrastructure.DataProtection;

namespace InitiativeScoping.Integration.Tests;

public class DataProtectionTests
{
    private static readonly CryptoKeyName Key = new("p", "us-central1", "ring", "data-protection");

    [Fact]
    public void Kms_encryptor_round_trips_key_xml_and_records_key_name()
    {
        var kms = new FakeKms();
        var plaintext = XElement.Parse("<key id=\"k1\"><descriptor>secret material</descriptor></key>");

        var encrypted = new KmsXmlEncryptor(kms, Key).Encrypt(plaintext);

        Assert.Equal(typeof(KmsXmlDecryptor), encrypted.DecryptorType);
        Assert.Equal(Key.ToString(), encrypted.EncryptedElement.Attribute("keyName")!.Value);
        Assert.DoesNotContain("secret material", encrypted.EncryptedElement.ToString());
        Assert.Equal(1, kms.EncryptCalls);

        var decrypted = new KmsXmlDecryptor(kms).Decrypt(encrypted.EncryptedElement);

        Assert.True(XNode.DeepEquals(plaintext, decrypted));
        Assert.Equal(Key, kms.LastDecryptKey);
    }

    [Fact]
    public void Kms_decryptor_rejects_malformed_elements()
    {
        var decryptor = new KmsXmlDecryptor(new FakeKms());
        Assert.Throws<InvalidOperationException>(() => decryptor.Decrypt(new XElement("kmsEncryptedKey")));
    }

    /// <summary>XOR "cipher" standing in for KMS so the wrapping contract can be tested offline.</summary>
    private sealed class FakeKms : KeyManagementServiceClient
    {
        public int EncryptCalls { get; private set; }
        public CryptoKeyName? LastDecryptKey { get; private set; }

        public override EncryptResponse Encrypt(EncryptRequest request, CallSettings? callSettings = null)
        {
            EncryptCalls++;
            return new EncryptResponse { Name = request.Name, Ciphertext = Scramble(request.Plaintext) };
        }

        public override DecryptResponse Decrypt(DecryptRequest request, CallSettings? callSettings = null)
        {
            LastDecryptKey = CryptoKeyName.Parse(request.Name);
            return new DecryptResponse { Plaintext = Scramble(request.Ciphertext) };
        }

        private static ByteString Scramble(ByteString input) =>
            ByteString.CopyFrom(input.ToByteArray().Select(b => (byte)(b ^ 0x5A)).ToArray());
    }
}
