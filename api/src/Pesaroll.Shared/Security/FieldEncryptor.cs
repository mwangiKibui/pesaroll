using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pesaroll.Shared.Security;

/// <summary>A field-encryption key. <see cref="Secret"/> is base64, exactly 32 bytes (openssl rand -base64 32).</summary>
public sealed class EncryptionKey
{
    public string Id { get; set; } = "";

    public string Secret { get; set; } = "";
}

/// <summary>
/// Settings under <c>Encryption</c>. The first key encrypts; every key decrypts, so a key can be
/// replaced by putting the new one first and re-encrypting old values before dropping the old key.
/// </summary>
public sealed class EncryptionOptions
{
    public List<EncryptionKey> Keys { get; set; } = [];
}

public sealed class FieldDecryptionException(string message) : Exception(message);

/// <summary>
/// Encrypts sensitive fields (bank account numbers now; ID numbers and KRA PINs with PLAT-05) with
/// AES-256-GCM before they reach the database. The context (for example the table, column and
/// company) is bound in, so stored bytes copied onto another row won't decrypt.
/// Stored as: version (1) · key id length (1) · key id · nonce (12) · tag (16) · ciphertext.
/// </summary>
public sealed class FieldEncryptor
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly (string Id, byte[] Key) current;
    private readonly Dictionary<string, byte[]> keys;

    public FieldEncryptor(IOptions<EncryptionOptions> options)
    {
        var configured = options.Value.Keys;
        if (configured.Count == 0)
        {
            throw new InvalidOperationException("Encryption:Keys is empty: set at least one field-encryption key.");
        }

        keys = [];
        foreach (var key in configured)
        {
            var bytes = new byte[key.Secret.Length];
            if (string.IsNullOrWhiteSpace(key.Id) || Encoding.UTF8.GetByteCount(key.Id) > 32
                || !Convert.TryFromBase64String(key.Secret, bytes, out var length) || length != 32)
            {
                throw new InvalidOperationException(
                    $"Encryption key '{key.Id}' needs an id (up to 32 characters) and a base64 secret of exactly 32 bytes (openssl rand -base64 32).");
            }

            keys[key.Id] = bytes[..length];
        }

        current = (configured[0].Id, keys[configured[0].Id]);
    }

    public byte[] Encrypt(string plaintext, string context)
    {
        var id = Encoding.UTF8.GetBytes(current.Id);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[2 + id.Length + NonceSize + TagSize + data.Length];
        output[0] = Version;
        output[1] = (byte)id.Length;
        id.CopyTo(output, 2);
        var nonce = output.AsSpan(2 + id.Length, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = output.AsSpan(2 + id.Length + NonceSize, TagSize);
        var ciphertext = output.AsSpan(2 + id.Length + NonceSize + TagSize);
        using var aes = new AesGcm(current.Key, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag, Encoding.UTF8.GetBytes(context));
        return output;
    }

    public string Decrypt(byte[] stored, string context)
    {
        if (stored.Length < 2 || stored[0] != Version || stored.Length < 2 + stored[1] + NonceSize + TagSize)
        {
            throw new FieldDecryptionException("Not an encrypted field.");
        }

        var idLength = stored[1];
        var id = Encoding.UTF8.GetString(stored, 2, idLength);
        if (!keys.TryGetValue(id, out var key))
        {
            throw new FieldDecryptionException($"Encryption key '{id}' is not configured.");
        }

        var nonce = stored.AsSpan(2 + idLength, NonceSize);
        var tag = stored.AsSpan(2 + idLength + NonceSize, TagSize);
        var ciphertext = stored.AsSpan(2 + idLength + NonceSize + TagSize);
        var data = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, data, Encoding.UTF8.GetBytes(context));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new FieldDecryptionException("The field was changed or belongs to another record.");
        }

        return Encoding.UTF8.GetString(data);
    }
}

public static class FieldEncryptionRegistration
{
    /// <summary>Field encryption from <c>Encryption:Keys</c>; the app refuses to start without a valid key.</summary>
    public static IServiceCollection AddFieldEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EncryptionOptions>().Bind(configuration.GetSection("Encryption"))
            .Validate(o => o.Keys.Count > 0, "Encryption:Keys is empty: set at least one field-encryption key.")
            .ValidateOnStart();
        services.AddSingleton<FieldEncryptor>();
        return services;
    }
}
