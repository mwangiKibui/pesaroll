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
    [InlineData("123456789", "123456789")]   // a Maisha Namba
    public void ID_numbers_are_7_or_8_digits_or_a_9_digit_Maisha_Namba(string typed, string stored) => Assert.Equal(stored, EmployeeNumbers.NationalId(typed));

    [Theory]
    [InlineData("123456")]
    [InlineData("1234567890")]
    [InlineData("A1234567")]
    public void Other_ID_numbers_are_refused(string typed) => Assert.Null(EmployeeNumbers.NationalId(typed));

    [Fact]
    public void NSSF_and_SHA_numbers_are_kept_simple()
    {
        Assert.Equal("201234567", EmployeeNumbers.Nssf("201 234 567"));   // usually 9 digits
        Assert.Equal("2012345678", EmployeeNumbers.Nssf("2012345678"));
        Assert.Null(EmployeeNumbers.Nssf("123456"));
        Assert.Null(EmployeeNumbers.Nssf("12345678901"));
        Assert.Null(EmployeeNumbers.Nssf("NSSF"));
        Assert.Equal("CR1234567-8", EmployeeNumbers.Sha("cr1234567-8"));
        Assert.Null(EmployeeNumbers.Sha("x"));
    }

    [Theory]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("0790 123 456", "+254790123456")]
    [InlineData("0748 123 456", "+254748123456")]
    [InlineData("0110 123 456", "+254110123456")]
    [InlineData("0115 123 456", "+254115123456")]
    [InlineData("0141 123 456", "+254141123456")]   // 2026 ranges
    [InlineData("254722000111", "+254722000111")]
    public void M_Pesa_numbers_are_Safaricom_lines(string typed, string stored) => Assert.Equal(stored, EmployeeNumbers.Mpesa(typed));

    [Theory]
    [InlineData("0733 123 456")]   // Airtel
    [InlineData("0100 123 456")]   // Airtel
    [InlineData("0744 123 456")]
    [InlineData("0771 123 456")]   // Telkom
    [InlineData("0712 345 67")]    // a digit short
    [InlineData("0201234567")]     // a landline
    public void Other_numbers_cant_take_M_Pesa(string typed) => Assert.Null(EmployeeNumbers.Mpesa(typed));

    [Fact]
    public void Shown_masked()
    {
        Assert.Equal("•••• 5678", EmployeeNumbers.Mask("12345678"));
        Assert.Equal("•••• 678Z", EmployeeNumbers.Mask("A012345678Z"));
    }
}
