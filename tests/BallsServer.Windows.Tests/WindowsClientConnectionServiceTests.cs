using BallsServer.Core.Sharing;

namespace BallsServer.Windows.Tests;

public sealed class WindowsClientConnectionServiceTests
{
    [Fact]
    public async Task ConnectUsesExactEndpointAndAuthenticatesOnce()
    {
        var platform = new RecordingClientPlatform();
        var state = new RecordingClientStateStore();
        var service = new WindowsClientConnectionService(platform, state);
        var request = new ClientConnectionRequest(Grant(), 'P', SaveCredential: true);

        var result = await service.ConnectAsync(request);

        Assert.Equal(ClientConnectionResultStatus.Connected, result.Status);
        Assert.Equal(1, platform.MapAttempts);
        Assert.Equal(@"\\owner-pc.example.ts.net\Balls", platform.MappedUnc);
        Assert.Equal("owner-pc.example.ts.net", platform.CredentialTarget);
        Assert.Equal(@"OWNER-PC\BallsClient-7H4K2M", platform.UserName);
        Assert.DoesNotContain(request.Grant.Password, state.Record?.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            ["credential-observe", "drive-observe", "credential-save", "map", "verify", "state-save"],
            platform.Events.Concat(state.Events));
    }

    [Fact]
    public async Task VerificationFailureRollsBackOnlyTheNewMappingAndCredential()
    {
        var platform = new RecordingClientPlatform { FailVerification = true };
        var state = new RecordingClientStateStore();
        var service = new WindowsClientConnectionService(platform, state);

        var result = await service.ConnectAsync(new ClientConnectionRequest(Grant(), 'P', true));

        Assert.Equal(ClientConnectionResultStatus.Failed, result.Status);
        Assert.Equal(1, platform.MapAttempts);
        Assert.True(platform.Unmapped);
        Assert.True(platform.CredentialDeleted);
        Assert.Null(state.Record);
    }

    private static SetupCodeGrant Grant() => new(
        SetupCodeCodec.CurrentVersion,
        AccessPathKind.Tailscale,
        "owner-pc.example.ts.net",
        "Balls",
        @"OWNER-PC\BallsClient-7H4K2M",
        "correct-horse-battery-staple-47",
        DateTimeOffset.UtcNow.AddMinutes(10));

    private sealed class RecordingClientPlatform : IWindowsClientPlatform
    {
        public List<string> Events { get; } = [];

        public int MapAttempts { get; private set; }

        public string? MappedUnc { get; private set; }

        public string? CredentialTarget { get; private set; }

        public string? UserName { get; private set; }

        public bool FailVerification { get; init; }

        public bool Unmapped { get; private set; }

        public bool CredentialDeleted { get; private set; }

        public IReadOnlyList<char> GetAvailableDriveLetters() => ['P'];

        public bool CredentialExists(string target)
        {
            Events.Add("credential-observe");
            return false;
        }

        public bool IsDriveLetterAvailable(char driveLetter)
        {
            Events.Add("drive-observe");
            return true;
        }

        public void SaveCredential(string target, string userName, string password)
        {
            Events.Add("credential-save");
            CredentialTarget = target;
            UserName = userName;
        }

        public void MapDrive(char driveLetter, string unc, string userName, string password)
        {
            Events.Add("map");
            MapAttempts++;
            MappedUnc = unc;
            UserName = userName;
        }

        public void VerifyRoundTrip(char driveLetter, string operationId)
        {
            Events.Add("verify");
            if (FailVerification)
            {
                throw new ClientPlatformException();
            }
        }

        public string? GetMappedUnc(char driveLetter) => MappedUnc;

        public void UnmapDrive(char driveLetter, string expectedUnc)
        {
            Unmapped = true;
        }

        public void DeleteCredential(string target)
        {
            CredentialDeleted = true;
        }
    }

    private sealed class RecordingClientStateStore : IClientConnectionStateStore
    {
        public List<string> Events { get; } = [];

        public ClientConnectionStateRecord? Record { get; private set; }

        public ClientConnectionStateRecord? Load() => Record;

        public void Save(ClientConnectionStateRecord record)
        {
            Events.Add("state-save");
            Record = record;
        }

        public void Delete() => Record = null;
    }
}
