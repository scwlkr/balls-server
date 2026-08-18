using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BallsServer.Core.Sharing;

public enum HostSetupOperation
{
    Apply,
    StopSharing,
}

public sealed record HostSetupRequest(
    int Version,
    string OperationId,
    string Nonce,
    string InitiatingUserSid,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    HostSetupOperation Operation,
    string? ManagedFolder,
    AccessPathKind? AccessPath)
{
    public override string ToString() =>
        $"HostSetupRequest {{ Version = {Version}, OperationId = {OperationId}, Nonce = [REDACTED], " +
        $"InitiatingUserSid = [REDACTED], IssuedAt = {IssuedAt:O}, ExpiresAt = {ExpiresAt:O}, " +
        $"Operation = {Operation}, ManagedFolder = [REDACTED], AccessPath = {AccessPath} }}";
}

public sealed record HostSetupResponse(
    int Version,
    string OperationId,
    string Nonce,
    HostSetupResult Result)
{
    public override string ToString() =>
        $"HostSetupResponse {{ Version = {Version}, OperationId = {OperationId}, Nonce = [REDACTED], " +
        $"Result = {Result} }}";
}

public static partial class HostSetupProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public const int CurrentVersion = 2;

    public static string EncodeRequest(HostSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request, request.IssuedAt);
        return JsonSerializer.Serialize(RequestEnvelope.FromRequest(request), JsonOptions);
    }

    public static HostSetupRequest DecodeRequest(string encoded, DateTimeOffset now)
    {
        var envelope = DeserializeStrict<RequestEnvelope>(encoded);
        var request = envelope.ToRequest();
        ValidateRequest(request, now);
        return request;
    }

    public static string EncodeResponse(HostSetupResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ValidateBinding(response.Version, response.OperationId, response.Nonce);
        ValidateResult(response.Result);
        return JsonSerializer.Serialize(ResponseEnvelope.FromResponse(response), JsonOptions);
    }

    public static HostSetupResponse DecodeResponse(string encoded)
    {
        var envelope = DeserializeStrict<ResponseEnvelope>(encoded);
        var response = envelope.ToResponse();
        ValidateBinding(response.Version, response.OperationId, response.Nonce);
        ValidateResult(response.Result);
        return response;
    }

    private static T DeserializeStrict<T>(string encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);

        try
        {
            using var document = JsonDocument.Parse(encoded);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The helper message is not an object.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new FormatException("The helper message contains a duplicate field.");
                }
            }

            return JsonSerializer.Deserialize<T>(encoded, JsonOptions) ??
                throw new FormatException("The helper message is incomplete.");
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new FormatException("The helper message is malformed.", exception);
        }
    }

    private static void ValidateRequest(HostSetupRequest request, DateTimeOffset now)
    {
        ValidateBinding(request.Version, request.OperationId, request.Nonce);
        if (!SidPattern().IsMatch(request.InitiatingUserSid) ||
            !Enum.IsDefined(request.Operation) ||
            request.Operation == HostSetupOperation.Apply &&
            (request.AccessPath is null ||
             !Enum.IsDefined(request.AccessPath.Value) ||
             string.IsNullOrWhiteSpace(request.ManagedFolder) ||
             !Path.IsPathFullyQualified(request.ManagedFolder)) ||
            request.Operation == HostSetupOperation.StopSharing &&
            (request.ManagedFolder is not null || request.AccessPath is not null))
        {
            throw new FormatException("The host setup request is invalid.");
        }

        var lifetime = request.ExpiresAt - request.IssuedAt;
        if (lifetime <= TimeSpan.Zero ||
            lifetime > TimeSpan.FromMinutes(3) ||
            request.ExpiresAt <= now ||
            request.IssuedAt > now.AddSeconds(5) ||
            request.IssuedAt < now.AddMinutes(-3))
        {
            throw new FormatException("The host setup request has expired.");
        }
    }

    private static void ValidateBinding(int version, string operationId, string nonce)
    {
        if (version != CurrentVersion ||
            !OperationIdPattern().IsMatch(operationId) ||
            !NoncePattern().IsMatch(nonce))
        {
            throw new FormatException("The helper message binding is invalid.");
        }
    }

    private static void ValidateResult(HostSetupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(result.Status) || result.Succeeded != (result.Status == HostSetupResultStatus.Completed))
        {
            throw new FormatException("The helper result is invalid.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperationIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex NoncePattern();

    [GeneratedRegex("^S-1-5(?:-[0-9]+){2,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex SidPattern();

    private sealed record RequestEnvelope
    {
        public required int Version { get; init; }

        public required string OperationId { get; init; }

        public required string Nonce { get; init; }

        public required string InitiatingUserSid { get; init; }

        public required DateTimeOffset IssuedAt { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public required HostSetupOperation Operation { get; init; }

        public string? ManagedFolder { get; init; }

        public AccessPathKind? AccessPath { get; init; }

        public static RequestEnvelope FromRequest(HostSetupRequest request) => new()
        {
            Version = request.Version,
            OperationId = request.OperationId,
            Nonce = request.Nonce,
            InitiatingUserSid = request.InitiatingUserSid,
            IssuedAt = request.IssuedAt,
            ExpiresAt = request.ExpiresAt,
            Operation = request.Operation,
            ManagedFolder = request.ManagedFolder,
            AccessPath = request.AccessPath,
        };

        public HostSetupRequest ToRequest() => new(
            Version,
            OperationId,
            Nonce,
            InitiatingUserSid,
            IssuedAt,
            ExpiresAt,
            Operation,
            ManagedFolder,
            AccessPath);
    }

    private sealed record ResponseEnvelope
    {
        public required int Version { get; init; }

        public required string OperationId { get; init; }

        public required string Nonce { get; init; }

        public required HostSetupResultStatus Status { get; init; }

        public string? SetupCode { get; init; }

        public static ResponseEnvelope FromResponse(HostSetupResponse response) => new()
        {
            Version = response.Version,
            OperationId = response.OperationId,
            Nonce = response.Nonce,
            Status = response.Result.Status,
            SetupCode = response.Result.SetupCode,
        };

        public HostSetupResponse ToResponse()
        {
            var result = Status switch
            {
                HostSetupResultStatus.Completed when SetupCode is { Length: > 0 } =>
                    HostSetupResult.Completed(SetupCode),
                HostSetupResultStatus.Canceled when SetupCode is null => HostSetupResult.Canceled(),
                HostSetupResultStatus.Refused when SetupCode is null => HostSetupResult.Refused(
                    "Host setup was refused because this computer is not in the required safe state."),
                HostSetupResultStatus.Failed when SetupCode is null => HostSetupResult.Failed(),
                HostSetupResultStatus.Stopped when SetupCode is null => HostSetupResult.Stopped(),
                _ => throw new FormatException("The helper result is invalid."),
            };

            return new HostSetupResponse(Version, OperationId, Nonce, result);
        }
    }
}
