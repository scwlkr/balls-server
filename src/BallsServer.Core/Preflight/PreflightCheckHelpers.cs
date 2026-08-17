using System.Globalization;

namespace BallsServer.Core.Preflight;

internal static class PreflightCheckHelpers
{
    public static PreflightCheckResult ProbeUnavailable<T>(
        ProbeResult<T> probe,
        PreflightCheckId id,
        string title)
        where T : notnull =>
        PreflightCheckResult.Unknown(
            id,
            title,
            probe.ErrorCode ?? "probe_unavailable",
            probe.ErrorMessage ?? "Windows did not return enough information to complete this check.");

    public static string YesNo(bool value) => value ? "Yes" : "No";

    public static string FormatBytes(long bytes)
    {
        const double gibibyte = 1024d * 1024d * 1024d;
        return string.Create(CultureInfo.InvariantCulture, $"{bytes / gibibyte:0.0} GiB");
    }
}
