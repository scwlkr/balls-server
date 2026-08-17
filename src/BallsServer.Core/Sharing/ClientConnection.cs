namespace BallsServer.Core.Sharing;

public sealed record ClientConnectionRequest(
    SetupCodeGrant Grant,
    char DriveLetter,
    bool SaveCredential)
{
    public override string ToString() =>
        $"ClientConnectionRequest {{ Grant = {Grant}, DriveLetter = {DriveLetter}, " +
        $"SaveCredential = {SaveCredential} }}";
}

public enum ClientConnectionResultStatus
{
    Connected,
    Disconnected,
    Canceled,
    Refused,
    Failed,
}

public sealed record ClientConnectionResult(
    ClientConnectionResultStatus Status,
    string PublicMessage,
    char? DriveLetter,
    string? Endpoint)
{
    public static ClientConnectionResult Connected(char driveLetter, string endpoint) => new(
        ClientConnectionResultStatus.Connected,
        $"Balls Server is connected as drive {char.ToUpperInvariant(driveLetter)}:.",
        char.ToUpperInvariant(driveLetter),
        endpoint);

    public static ClientConnectionResult Disconnected() => new(
        ClientConnectionResultStatus.Disconnected,
        "Balls Server is disconnected from this Windows profile.",
        null,
        null);

    public static ClientConnectionResult Canceled() => new(
        ClientConnectionResultStatus.Canceled,
        "The connection was canceled before it changed this Windows profile.",
        null,
        null);

    public static ClientConnectionResult Refused(string publicMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicMessage);
        return new(ClientConnectionResultStatus.Refused, publicMessage, null, null);
    }

    public static ClientConnectionResult Failed() => new(
        ClientConnectionResultStatus.Failed,
        "The connection did not finish. No alternate endpoint was attempted.",
        null,
        null);
}

public interface IClientConnectionService
{
    IReadOnlyList<char> GetAvailableDriveLetters();

    Task<ClientConnectionResult> ConnectAsync(
        ClientConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<ClientConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default);
}
