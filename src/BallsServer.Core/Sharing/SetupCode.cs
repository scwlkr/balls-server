using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BallsServer.Core.Sharing;

public enum AccessPathKind
{
    Local,
    Tailscale,
}

public sealed record SetupCodeGrant(
    int Version,
    AccessPathKind AccessPath,
    string HostName,
    string ShareName,
    string UserName,
    string Password,
    DateTimeOffset ExpiresAt)
{
    public override string ToString() =>
        $"SetupCodeGrant {{ Version = {Version}, AccessPath = {AccessPath}, HostName = {HostName}, " +
        $"ShareName = {ShareName}, UserName = {UserName}, Password = [REDACTED], ExpiresAt = {ExpiresAt:O} }}";
}

public static class SetupCodeCodec
{
    private const string Prefix = "BALLS1.";

    public const int CurrentVersion = 1;

    public static string Encode(SetupCodeGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var json = JsonSerializer.SerializeToUtf8Bytes(grant);
        return Prefix + Base64UrlEncode(json);
    }

    public static SetupCodeGrant Decode(string encoded, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);

        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("That setup code is not a supported Balls Server code.");
        }

        try
        {
            var grant = JsonSerializer.Deserialize<SetupCodeGrant>(Base64UrlDecode(encoded[Prefix.Length..])) ??
                throw new FormatException("That setup code is incomplete.");

            if (grant.Version != CurrentVersion)
            {
                throw new FormatException("That setup code version is not supported.");
            }

            if (grant.ExpiresAt <= now)
            {
                throw new FormatException("That setup code has expired. Create a new access grant on the host.");
            }

            if (!Enum.IsDefined(grant.AccessPath) ||
                grant.ShareName != "Balls" ||
                string.IsNullOrEmpty(grant.UserName) ||
                !Regex.IsMatch(
                    grant.UserName,
                    "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?\\\\BallsClient-[A-Z0-9]{6}$",
                    RegexOptions.CultureInvariant) ||
                string.IsNullOrEmpty(grant.Password) ||
                grant.Password.Length is < 20 or > 128 ||
                grant.Password.Any(static character => character is < '!' or > '~'))
            {
                throw new FormatException("That setup code contains an invalid limited credential.");
            }

            if (!IsSupportedHost(grant.AccessPath, grant.HostName))
            {
                throw new FormatException("That setup code contains a public or unsupported host.");
            }

            return grant;
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new FormatException("That setup code is malformed.", exception);
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("That setup code is malformed."),
        };

        return Convert.FromBase64String(base64);
    }

    private static bool IsSupportedHost(AccessPathKind accessPath, string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return false;
        }

        if (!IPAddress.TryParse(hostName, out _))
        {
            if (Uri.CheckHostName(hostName) != UriHostNameType.Dns)
            {
                return false;
            }

            return accessPath switch
            {
                AccessPathKind.Local => Regex.IsMatch(
                    hostName,
                    "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?$",
                    RegexOptions.CultureInvariant),
                AccessPathKind.Tailscale => hostName.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        return false;
    }
}
