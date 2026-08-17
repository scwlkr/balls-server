using System.Text.RegularExpressions;

namespace BallsServer.App.Tests;

public sealed class HostCredentialGeneratorTests
{
    [Fact]
    public void GeneratorCreatesBoundedLimitedIdentityAndComplexPassword()
    {
        var userName = BallsServer.Helper.HostCredentialGenerator.CreateUserName();
        var password = BallsServer.Helper.HostCredentialGenerator.CreatePassword();

        Assert.Matches("^BallsClient-[A-Z0-9]{6}$", userName);
        Assert.Equal(28, password.Length);
        Assert.Matches("^[A-Z]", password);
        Assert.Matches("^[A-Z][a-z]", password);
        Assert.True(char.IsAsciiDigit(password[2]));
        Assert.Matches(new Regex("^[A-Z][a-z][0-9][^A-Za-z0-9]", RegexOptions.CultureInvariant), password);
        Assert.All(password, character => Assert.InRange(character, '!', '~'));
    }
}
