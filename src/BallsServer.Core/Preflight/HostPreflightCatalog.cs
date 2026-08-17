namespace BallsServer.Core.Preflight;

public static class HostPreflightCatalog
{
    public static IReadOnlyList<PreflightCheckId> OrderedCheckIds { get; } = Array.AsReadOnly(
    [
        PreflightCheckId.Administrator,
        PreflightCheckId.WindowsVersion,
        PreflightCheckId.Storage,
        PreflightCheckId.NetworkProfile,
        PreflightCheckId.Firewall,
        PreflightCheckId.Tailscale,
        PreflightCheckId.Smb,
        PreflightCheckId.FolderPermissions,
    ]);

    public static IPreflightService CreateService(
        HostPreflightProbes probes,
        PreflightPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(probes);

        IPreflightCheck[] checks =
        [
            new AdministratorPreflightCheck(probes.Administrator),
            new WindowsVersionPreflightCheck(probes.WindowsVersion),
            new StoragePreflightCheck(probes.Storage),
            new NetworkProfilePreflightCheck(probes.NetworkProfile),
            new FirewallPreflightCheck(probes.Firewall),
            new TailscalePreflightCheck(probes.Tailscale),
            new SmbPreflightCheck(probes.Smb),
            new FolderPermissionPreflightCheck(probes.FolderPermissions),
        ];

        return new PreflightService(checks, policy ?? PreflightPolicy.HostDefault, timeProvider);
    }

    internal static void Validate(IReadOnlyList<IPreflightCheck> checks)
    {
        var actual = checks.Select(static check => check.Id).ToArray();

        if (!actual.SequenceEqual(OrderedCheckIds))
        {
            throw new ArgumentException(
                "A host preflight must contain each required check exactly once and in the defined order.",
                nameof(checks));
        }
    }
}
