namespace BallsServer.Core.Sharing;

public enum HostSetupResultStatus
{
    Completed,
    Canceled,
    Refused,
    Failed,
}

public sealed record HostSetupResult(
    HostSetupResultStatus Status,
    string PublicMessage,
    string? SetupCode)
{
    public bool Succeeded => Status == HostSetupResultStatus.Completed &&
        !string.IsNullOrWhiteSpace(SetupCode);

    public static HostSetupResult Completed(string setupCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupCode);
        return new(
            HostSetupResultStatus.Completed,
            "Host setup completed. Copy the one-time setup code to the approved client.",
            setupCode);
    }

    public static HostSetupResult Canceled() => new(
        HostSetupResultStatus.Canceled,
        "Host setup was canceled. No new setup was applied.",
        null);

    public static HostSetupResult Refused(string publicMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicMessage);
        return new(HostSetupResultStatus.Refused, publicMessage, null);
    }

    public static HostSetupResult Failed() => new(
        HostSetupResultStatus.Failed,
        "Host setup did not finish. Review the host state before trying again.",
        null);

    public override string ToString() =>
        $"HostSetupResult {{ Status = {Status}, PublicMessage = {PublicMessage}, SetupCode = " +
        (SetupCode is null ? "null" : "[REDACTED]") + " }";
}
