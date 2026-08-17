using System.Security.Cryptography;

namespace BallsServer.Helper;

public static class HostCredentialGenerator
{
    private const string NameAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string PasswordAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+,-.:=?@_";
    private const string UppercaseAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowercaseAlphabet = "abcdefghijkmnopqrstuvwxyz";
    private const string DigitAlphabet = "23456789";
    private const string SymbolAlphabet = "!#%+,-.:=?@_";

    public static string CreateUserName() => "BallsClient-" + RandomString(NameAlphabet, 6);

    public static string CreatePassword()
    {
        var password = RandomString(PasswordAlphabet, 28).ToCharArray();
        password[0] = RandomCharacter(UppercaseAlphabet);
        password[1] = RandomCharacter(LowercaseAlphabet);
        password[2] = RandomCharacter(DigitAlphabet);
        password[3] = RandomCharacter(SymbolAlphabet);
        return new string(password);
    }

    private static string RandomString(string alphabet, int length)
    {
        return string.Create(length, (alphabet, length), static (span, state) =>
        {
            for (var index = 0; index < state.length; index++)
            {
                span[index] = state.alphabet[RandomNumberGenerator.GetInt32(state.alphabet.Length)];
            }
        });
    }

    private static char RandomCharacter(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
