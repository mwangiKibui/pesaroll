using Microsoft.Extensions.Options;
using Pesaroll.Shared.Security;

namespace Pesaroll.Organizations.Tests;

/// <summary>Field encryption (bank account numbers now, PLAT-05 for employees' details later).</summary>
public sealed class FieldEncryptorTests
{
    private static EncryptionKey Key(string id, byte fill) => new() { Id = id, Secret = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()) };

    private static FieldEncryptor With(params EncryptionKey[] keys) => new(Options.Create(new EncryptionOptions { Keys = [.. keys] }));

    [Fact]
    public void A_value_comes_back_only_with_the_same_context_and_never_appears_in_the_stored_bytes()
    {
        var encryptor = With(Key("2026-10", 1));

        var stored = encryptor.Encrypt("0123456789", "company:42");

        Assert.Equal("0123456789", encryptor.Decrypt(stored, "company:42"));
        Assert.DoesNotContain("0123456789", System.Text.Encoding.UTF8.GetString(stored));
        // Copying the bytes onto another row (another context) doesn't work.
        Assert.Throws<FieldDecryptionException>(() => encryptor.Decrypt(stored, "company:43"));
    }

    [Fact]
    public void The_same_value_encrypts_differently_each_time()
    {
        var encryptor = With(Key("2026-10", 1));
        Assert.NotEqual(encryptor.Encrypt("0123456789", "c"), encryptor.Encrypt("0123456789", "c"));
    }

    [Fact]
    public void Changed_bytes_are_refused()
    {
        var encryptor = With(Key("2026-10", 1));
        var stored = encryptor.Encrypt("0123456789", "c");
        stored[^1] ^= 1;
        Assert.Throws<FieldDecryptionException>(() => encryptor.Decrypt(stored, "c"));
    }

    [Fact]
    public void After_a_key_change_old_values_still_read_and_new_ones_use_the_new_key()
    {
        var old = With(Key("2026-10", 1)).Encrypt("0123456789", "c");
        var rotated = With(Key("2027-01", 2), Key("2026-10", 1));

        Assert.Equal("0123456789", rotated.Decrypt(old, "c"));
        var fresh = rotated.Encrypt("0123456789", "c");
        Assert.Throws<FieldDecryptionException>(() => With(Key("2026-10", 1)).Decrypt(fresh, "c"));
    }

    [Fact]
    public void Refuses_to_start_without_a_proper_key()
    {
        Assert.Throws<InvalidOperationException>(() => With());
        Assert.Throws<InvalidOperationException>(() => With(new EncryptionKey { Id = "short", Secret = Convert.ToBase64String(new byte[16]) }));
        Assert.Throws<InvalidOperationException>(() => With(new EncryptionKey { Id = "", Secret = Convert.ToBase64String(new byte[32]) }));
    }
}
