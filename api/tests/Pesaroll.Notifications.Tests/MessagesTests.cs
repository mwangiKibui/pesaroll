namespace Pesaroll.Notifications.Tests;

/// <summary>
/// NTF-01: what goes into an email or SMS. Recipients are checked and stored one way, SMS are
/// counted in the parts carriers bill for, and messages come from templates with no salaries.
/// </summary>
public sealed class MessagesTests
{
    [Theory]
    [InlineData("0712 345 678", "+254712345678")]
    [InlineData("+254712345678", "+254712345678")]
    [InlineData("254-712-345-678", "+254712345678")]
    [InlineData("0110345678", "+254110345678")]   // the newer 01 numbers
    [InlineData("712345678", "+254712345678")]
    public void Kenyan_mobile_numbers_are_stored_as_plus_254(string typed, string stored) =>
        Assert.Equal(stored, Recipients.KenyanMobile(typed));

    [Theory]
    [InlineData("0201234567")]      // a Nairobi landline
    [InlineData("071234567")]       // a digit short
    [InlineData("+256712345678")]   // Uganda
    [InlineData("call me")]
    [InlineData("")]
    public void Other_numbers_are_refused(string typed) => Assert.Null(Recipients.KenyanMobile(typed));

    [Theory]
    [InlineData(" Wanjiru@Example.com ", "wanjiru@example.com")]
    [InlineData("payroll@mama-oliech.co.ke", "payroll@mama-oliech.co.ke")]
    public void Emails_are_trimmed_and_lower_cased(string typed, string stored) => Assert.Equal(stored, Recipients.Email(typed));

    [Theory]
    [InlineData("wanjiru")]
    [InlineData("wanjiru@")]
    [InlineData("Wanjiru <wanjiru@example.com>")]
    public void Other_emails_are_refused(string typed) => Assert.Null(Recipients.Email(typed));

    [Fact]
    public void Logs_only_see_masked_recipients()
    {
        Assert.Equal("+254 7•• ••• 678", Recipients.Mask("+254712345678"));
        Assert.Equal("w•••@example.com", Recipients.Mask("wanjiru@example.com"));
    }

    [Theory]
    [InlineData(160, 'a', 1)]
    [InlineData(161, 'a', 2)]    // long messages go in parts of 153
    [InlineData(306, 'a', 2)]
    [InlineData(307, 'a', 3)]
    [InlineData(70, 'ü', 1)]     // ü is in the GSM alphabet
    public void Sms_are_counted_in_billed_parts(int length, char letter, int parts) =>
        Assert.Equal(parts, SmsParts.Count(new string(letter, length)));

    [Fact]
    public void Characters_outside_the_GSM_alphabet_make_shorter_parts()
    {
        Assert.Equal(1, SmsParts.Count("Malipo 💰" + new string('a', 60)));     // 70 UCS-2 characters
        Assert.Equal(2, SmsParts.Count("Malipo 💰" + new string('a', 70)));
        Assert.Equal(1, SmsParts.Count(new string('a', 150) + "[]{}~"));        // extended characters take two: 160
        Assert.Equal(2, SmsParts.Count(new string('a', 151) + "[]{}~"));
    }

    [Fact]
    public void The_payroll_reminder_says_what_to_do_and_by_when()
    {
        var data = new Dictionary<string, string>
        {
            ["organization"] = "Mama Oliech Group", ["schedule"] = "Monthly", ["payDate"] = "Fri 30 Oct 2026", ["link"] = "https://app.pesaroll.co.ke",
        };

        var email = Templates.Render("payroll_reminder", "en", data);
        var sms = Templates.Render("payroll_reminder", "sw", data);

        Assert.Equal("Reminder: run payroll for Monthly by Fri 30 Oct 2026", email.Subject);
        Assert.Contains("Mama Oliech Group", email.Text);
        Assert.Contains("https://app.pesaroll.co.ke", email.Html);
        Assert.Equal(1, SmsParts.Count(Templates.Render("payroll_reminder", "en", data).Sms));   // one SMS, one charge
        Assert.StartsWith("Pesaroll: kumbusho", sms.Sms);   // Swahili, for when the app offers it
    }

    [Fact]
    public void Template_values_are_escaped_in_html()
    {
        var html = Templates.Render("payroll_reminder", "en", new Dictionary<string, string>
        {
            ["organization"] = "<script>x</script>", ["schedule"] = "M&M", ["payDate"] = "today", ["link"] = "https://app.pesaroll.co.ke",
        }).Html;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("M&amp;M", html);
    }
}
