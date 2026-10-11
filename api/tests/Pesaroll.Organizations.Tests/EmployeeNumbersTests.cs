namespace Pesaroll.Organizations.Tests;

/// <summary>ONB-09: an employee's statutory numbers are checked and stored one way.</summary>
public sealed class EmployeeNumbersTests
{
    [Theory]
    [InlineData("a012345678z", "A012345678Z")]
    [InlineData(" A 012345678 Z ", "A012345678Z")]
    public void A_persons_KRA_PIN_starts_with_A(string typed, string stored) => Assert.Equal(stored, EmployeeNumbers.KraPin(typed));

    [Theory]
    [InlineData("P051234567X")]   // a company's PIN
    [InlineData("A01234567Z")]    // a digit short
    [InlineData("12345678")]
    public void Other_PINs_are_refused(string typed) => Assert.Null(EmployeeNumbers.KraPin(typed));

    [Theory]
    [InlineData("12345678", "12345678")]
    [InlineData("1234567", "1234567")]
    [InlineData("12 345 678", "12345678")]
    public void ID_numbers_are_7_or_8_digits(string typed, string stored) => Assert.Equal(stored, EmployeeNumbers.NationalId(typed));

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("A1234567")]
    public void Other_ID_numbers_are_refused(string typed) => Assert.Null(EmployeeNumbers.NationalId(typed));

    [Fact]
    public void NSSF_and_SHA_numbers_are_kept_simple()
    {
        Assert.Equal("2012345678", EmployeeNumbers.Nssf("2012 345 678"));
        Assert.Null(EmployeeNumbers.Nssf("NSSF"));
        Assert.Equal("CR1234567-8", EmployeeNumbers.Sha("cr1234567-8"));
        Assert.Null(EmployeeNumbers.Sha("x"));
    }

    [Fact]
    public void Shown_masked()
    {
        Assert.Equal("•••• 5678", EmployeeNumbers.Mask("12345678"));
        Assert.Equal("•••• 678Z", EmployeeNumbers.Mask("A012345678Z"));
    }
}
