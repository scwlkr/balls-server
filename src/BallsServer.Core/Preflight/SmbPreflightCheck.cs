namespace BallsServer.Core.Preflight;

public sealed class SmbPreflightCheck(ISmbProbe probe) : IPreflightCheck
{
    public PreflightCheckId Id => PreflightCheckId.Smb;

    public string Title => "SMB file sharing";

    public int Order => 70;

    public async ValueTask<PreflightCheckResult> CheckAsync(
        PreflightContext context,
        CancellationToken cancellationToken)
    {
        var probeResult = await probe.ObserveAsync(cancellationToken).ConfigureAwait(false);
        if (!probeResult.HasValue)
        {
            return PreflightCheckHelpers.ProbeUnavailable(probeResult, Id, Title);
        }

        var observation = probeResult.Value!;
        var dialectRange = observation.DialectRange;
        var evidence = new[]
        {
            new PreflightEvidence("Server service", observation.ServerServiceState.ToString()),
            new PreflightEvidence("SMB 2/3 enabled", FormatNullable(observation.IsSmb2Enabled)),
            new PreflightEvidence("SMB 1 enabled", FormatNullable(observation.IsSmb1Enabled)),
            new PreflightEvidence("Minimum SMB 2/3 dialect", FormatDialect(dialectRange.Minimum)),
            new PreflightEvidence("Maximum SMB 2/3 dialect", FormatDialect(dialectRange.Maximum)),
            new PreflightEvidence("Encryption required", FormatNullable(observation.EncryptData)),
        };

        if (observation.ServerServiceState is not (WindowsServiceState.Unknown or WindowsServiceState.Running))
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb_server_not_running",
                "The Windows Server service is not running.",
                evidence);
        }

        if (observation.IsSmb2Enabled is false)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb2_disabled",
                "SMB 2/3 must be enabled before this computer can host files.",
                evidence);
        }

        if (observation.IsSmb1Enabled is true)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb1_enabled",
                "SMB 1 is enabled and must be disabled before this computer can host files.",
                evidence);
        }

        if (dialectRange.Minimum is SmbDialect.NoLimit or SmbDialect.Smb202 or SmbDialect.Smb210)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb_minimum_below_3",
                "The minimum accepted SMB dialect permits negotiation below SMB 3.0.",
                evidence);
        }

        if (dialectRange.Maximum is SmbDialect.Smb202 or SmbDialect.Smb210)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb_maximum_below_3",
                "The maximum accepted SMB dialect is below SMB 3.0.",
                evidence);
        }

        if (IsFiniteDialect(dialectRange.Minimum) &&
            IsFiniteDialect(dialectRange.Maximum) &&
            dialectRange.Minimum!.Value > dialectRange.Maximum!.Value)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.ActionRequired,
                "smb_dialect_range_contradictory",
                "The accepted SMB dialect range is contradictory.",
                evidence);
        }

        if (observation.ServerServiceState == WindowsServiceState.Unknown)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "smb_server_state_unknown",
                "Windows did not report a recognized Server service state.",
                evidence);
        }

        if (observation.IsSmb2Enabled is null)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "smb2_state_unknown",
                "Windows did not report whether SMB 2/3 is enabled.",
                evidence);
        }

        if (observation.IsSmb1Enabled is null)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "smb1_state_unknown",
                "Windows did not report whether SMB 1 is enabled.",
                evidence);
        }

        if (dialectRange.Minimum is null or SmbDialect.Unknown)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "smb_minimum_dialect_unknown",
                "Windows did not report a recognized minimum SMB 2/3 dialect.",
                evidence);
        }

        if (dialectRange.Maximum is null or SmbDialect.Unknown)
        {
            return PreflightCheckResult.Create(
                Id,
                Title,
                PreflightCheckStatus.Unknown,
                "smb_maximum_dialect_unknown",
                "Windows did not report a recognized maximum SMB 2/3 dialect.",
                evidence);
        }

        return PreflightCheckResult.Create(
            Id,
            Title,
            PreflightCheckStatus.Ready,
            "smb3_policy_satisfied",
            "The Windows SMB server accepts only SMB 3.0 or newer dialects.",
            evidence);
    }

    private static string FormatNullable(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown",
    };

    private static string FormatDialect(SmbDialect? dialect) => dialect switch
    {
        SmbDialect.NoLimit => "No limit",
        SmbDialect.Smb202 => "SMB 2.0.2",
        SmbDialect.Smb210 => "SMB 2.1",
        SmbDialect.Smb300 => "SMB 3.0",
        SmbDialect.Smb302 => "SMB 3.0.2",
        SmbDialect.Smb311 => "SMB 3.1.1",
        _ => "Unknown",
    };

    private static bool IsFiniteDialect(SmbDialect? dialect) => dialect is
        SmbDialect.Smb202 or
        SmbDialect.Smb210 or
        SmbDialect.Smb300 or
        SmbDialect.Smb302 or
        SmbDialect.Smb311;
}
