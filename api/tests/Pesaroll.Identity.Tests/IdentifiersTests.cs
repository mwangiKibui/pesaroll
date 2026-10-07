namespace Pesaroll.Identity.Tests;

/// <summary>How typed emails and phone numbers are normalised. Unit tests: no database.</summary>
public sealed class IdentifiersTests
{
    [Theory]
    [InlineData(" Wanjiru@Example.COM ", "wanjiru@example.com")]
    [InlineData("a.b@kampuni.co.ke", "a.b@kampuni.co.ke")]
    [InlineData("no-at-sign", null)]
    [InlineData("name@localhost", null)]
    [InlineData("Name <name@example.com>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Emails_are_trimmed_and_lowercased_or_refused(string? input, string? expected)
    {
        Assert.Equal(expected, Identifiers.NormalizeEmail(input));
    }

    [Theory]
    [InlineData("0712345678", "+254712345678")]
    [InlineData("0112345678", "+254112345678")]
    [InlineData("+254 712 345 678", "+254712345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("712345678", "+254712345678")]
    [InlineData("0812345678", null)]     // not a mobile prefix
    [InlineData("071234567", null)]      // too short
    [InlineData("+256712345678", null)]  // Uganda: comes with its rule pack
    [InlineData("   ", null)]
    public void Kenyan_phones_become_plus_254_or_are_refused(string input, string? expected)
    {
        Assert.Equal(expected, Identifiers.NormalizePhone(input));
    }

    [Theory]
    [InlineData("Wanjiru@Example.com", "wanjiru@example.com")]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("wanjiru", null)]
    public void Sign_in_accepts_either_an_email_or_a_phone(string input, string? expected)
    {
        Assert.Equal(expected, Identifiers.NormalizeEither(input));
    }
}
