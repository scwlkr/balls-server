using System.Net;
using System.Net.Sockets;
using System.Text.Json;

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

        if (!IPAddress.TryParse(hostName, out var address))
        {
            return Uri.CheckHostName(hostName) == UriHostNameType.Dns;
        }

        var bytes = address.GetAddressBytes();
        if (accessPath == AccessPathKind.Tailscale)
        {
            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => bytes[0] == 100 && (bytes[1] & 0b1100_0000) == 64,
                AddressFamily.InterNetworkV6 => bytes.AsSpan(0, 6).SequenceEqual(
                    new byte[] { 0xfd, 0x7a, 0x11, 0x5c, 0xa1, 0xe0 }),
                _ => false,
            };
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10 ||
                bytes[0] == 192 && bytes[1] == 168 ||
                bytes[0] == 172 && bytes[1] is >= 16 and <= 31,
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc,
            _ => false,
        };
    }
}
