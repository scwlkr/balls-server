using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BallsServer.Windows.Tests;

public sealed class HostOwnershipPolicyTests
{
    [Fact]
    public void FolderIdentityUsesTheStableWindowsVolumeAndFileIdentifier()
    {
        var first = WindowsFolderIdentity.Read(Path.GetTempPath());
        var second = WindowsFolderIdentity.Read(Path.GetFullPath(Path.GetTempPath()));

        Assert.Matches("^[0-9a-f]{8}:[0-9a-f]{16}$", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void StopSharingRequiresExactStableOwnershipAndRemovesOwnedMembership()
    {
        var policyType = Type.GetType(
            "BallsServer.Windows.HostOwnershipPolicy, BallsServer.Windows",
            throwOnError: false);
        var evaluate = policyType?.GetMethod(
            "EvaluateJson",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(evaluate);
        var preview = Evaluate(evaluate, ValidInput("Preview"));
        var approvedDigest = preview.RootElement.GetProperty("planDigest").GetString();
        var executeInput = JsonNode.Parse(ValidInput("Execute"))!.AsObject();
        executeInput["approvedPlanDigest"] = approvedDigest;
        using var authorized = Evaluate(evaluate, executeInput.ToJsonString());

        Assert.Equal("Ready", authorized.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            authorized.RootElement.GetProperty("primitives").EnumerateArray(),
            primitive => primitive.GetProperty("kind").GetString() == "RemoveMembership");
        Assert.All(
            authorized.RootElement.GetProperty("primitives").EnumerateArray()
                .Where(primitive => primitive.GetProperty("kind").GetString() != "MarkHostRemoved"),
            primitive => Assert.Equal(
                "Started",
                primitive.GetProperty("journalPhase").GetString()));

        var substituted = JsonNode.Parse(executeInput.ToJsonString())!.AsObject();
        substituted["live"]!["resources"]![0]!["stableId"] = "substituted-stable-id";
        using var refused = Evaluate(evaluate, substituted.ToJsonString());

        Assert.Equal("Refused", refused.RootElement.GetProperty("status").GetString());
        Assert.Empty(refused.RootElement.GetProperty("primitives").EnumerateArray());
    }

    [Fact]
    public void RepeatedSetupVerifiesEveryOwnedFingerprintAndCurrentEndpoint()
    {
        var input = JsonNode.Parse(ValidInput("Preview"))!.AsObject();
        input["operation"] = "Apply";

        using var exact = Evaluate(input.ToJsonString());

        Assert.Equal("NoChanges", exact.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["VerifyEffectiveAccess"],
            exact.RootElement.GetProperty("primitives").EnumerateArray()
                .Select(primitive => primitive.GetProperty("kind").GetString()!).ToArray());

        foreach (var mutation in new Action<JsonObject>[]
        {
            value => value["live"]!["resources"]![1]!["fingerprint"] = Hash('7'),
            value => value["live"]!["resources"]![2]!["fingerprint"] = Hash('8'),
            value => value["live"]!["unrelatedAclFingerprint"] = Hash('9'),
            value => value["live"]!["resources"]![4]!["fingerprint"] = Hash('a'),
            value => value["live"]!["resources"]![5]!["fingerprint"] = Hash('b'),
            value => value["live"]!["endpointFingerprint"] = Hash('c'),
        })
        {
            var changed = JsonNode.Parse(input.ToJsonString())!.AsObject();
            mutation(changed);
            using var refused = Evaluate(changed.ToJsonString());
            Assert.Equal("Refused", refused.RootElement.GetProperty("status").GetString());
        }

        var unapproved = JsonNode.Parse(input.ToJsonString())!.AsObject();
        unapproved["phase"] = "Execute";
        unapproved["approvedPlanDigest"] = Hash('f');
        using var refusedApproval = Evaluate(unapproved.ToJsonString());
        Assert.Equal("Refused", refusedApproval.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("minimumDialect")]
    [InlineData("maximumDialect")]
    [InlineData("signingEnabled")]
    [InlineData("signingRequired")]
    [InlineData("guestDisabled")]
    [InlineData("anonymousDisabled")]
    [InlineData("blankPasswordsDisabled")]
    [InlineData("authenticatedEffectiveAccess")]
    [InlineData("descendantReparseCount")]
    [InlineData("otherShareCount")]
    [InlineData("conflictingAceCount")]
    public void KnownUnsafeHostStateRefusesBeforeAuthorization(string field)
    {
        var input = JsonNode.Parse(ValidInput("Preview"))!.AsObject();
        var live = input["live"]!.AsObject();
        live[field] = field switch
        {
            "minimumDialect" => 767,
            "maximumDialect" => 767,
            "descendantReparseCount" or "otherShareCount" or "conflictingAceCount" => 1,
            _ => false,
        };

        using var result = Evaluate(input.ToJsonString());

        Assert.Equal("Refused", result.RootElement.GetProperty("status").GetString());
        Assert.Empty(result.RootElement.GetProperty("primitives").EnumerateArray());
    }

    [Fact]
    public void IncompleteObservationOrIncompleteOwnershipIsUnknown()
    {
        var incompleteObservation = JsonNode.Parse(ValidInput("Preview"))!.AsObject();
        incompleteObservation["live"]!["complete"] = false;
        var incompleteLedger = JsonNode.Parse(ValidInput("Preview"))!.AsObject();
        incompleteLedger["ledger"]!.AsObject().Remove("productHostId");
        var wrongResourceKinds = JsonNode.Parse(ValidInput("Preview"))!.AsObject();
        wrongResourceKinds["live"]!["resources"]![0]!["kind"] = "UnknownKind";

        using var observationResult = Evaluate(incompleteObservation.ToJsonString());
        using var ledgerResult = Evaluate(incompleteLedger.ToJsonString());
        using var resourceResult = Evaluate(wrongResourceKinds.ToJsonString());

        Assert.Equal("Unknown", observationResult.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unknown", ledgerResult.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unknown", resourceResult.RootElement.GetProperty("status").GetString());
    }

    private static JsonDocument Evaluate(MethodInfo method, string input)
    {
        var output = Assert.IsType<string>(method.Invoke(null, [input]));
        return JsonDocument.Parse(output);
    }

    private static JsonDocument Evaluate(string input) =>
        JsonDocument.Parse(BallsServer.Windows.HostOwnershipPolicy.EvaluateJson(input));

    private static string Hash(char value) => new(value, 64);

    private static string ValidInput(string phase) => $$"""
        {
          "phase":"{{phase}}",
          "operation":"StopSharing",
          "approvedPlanDigest":null,
          "ledger":{
            "schemaVersion":2,
            "productHostId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "revision":1,
            "status":"Committed",
            "desiredStateFingerprint":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "managedFolderStableId":"volume-1:file-1",
            "managedFolderFingerprint":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            "unrelatedAclFingerprint":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            "endpointFingerprint":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            "resources":[
              {"kind":"Group","stableId":"S-1-5-21-10-20-30-1001","fingerprint":"1111111111111111111111111111111111111111111111111111111111111111"},
              {"kind":"Account","stableId":"S-1-5-21-10-20-30-1002","fingerprint":"2222222222222222222222222222222222222222222222222222222222222222"},
              {"kind":"Membership","stableId":"S-1-5-21-10-20-30-1001:S-1-5-21-10-20-30-1002","fingerprint":"3333333333333333333333333333333333333333333333333333333333333333"},
              {"kind":"FolderAce","stableId":"ace-0000000000000000000000000001","fingerprint":"4444444444444444444444444444444444444444444444444444444444444444"},
              {"kind":"Share","stableId":"share-00000000000000000000000001","fingerprint":"5555555555555555555555555555555555555555555555555555555555555555"},
              {"kind":"FirewallRule","stableId":"rule-000000000000000000000000001","fingerprint":"6666666666666666666666666666666666666666666666666666666666666666"}
            ],
            "appliedPrimitives":[],
            "startedPrimitive":null
          },
          "live":{
            "complete":true,
            "serverRunning":true,
            "smb1Disabled":true,
            "smb2Enabled":true,
            "minimumDialect":768,
            "maximumDialect":785,
            "signingEnabled":true,
            "signingRequired":true,
            "guestDisabled":true,
            "anonymousDisabled":true,
            "blankPasswordsDisabled":true,
            "firewallScopeSafe":true,
            "authenticatedEffectiveAccess":true,
            "managedFolderStableId":"volume-1:file-1",
            "managedFolderFingerprint":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            "unrelatedAclFingerprint":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            "endpointFingerprint":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            "descendantReparseCount":0,
            "otherShareCount":0,
            "conflictingAceCount":0,
            "resources":[
              {"kind":"Group","state":"Present","stableId":"S-1-5-21-10-20-30-1001","fingerprint":"1111111111111111111111111111111111111111111111111111111111111111"},
              {"kind":"Account","state":"Present","stableId":"S-1-5-21-10-20-30-1002","fingerprint":"2222222222222222222222222222222222222222222222222222222222222222"},
              {"kind":"Membership","state":"Present","stableId":"S-1-5-21-10-20-30-1001:S-1-5-21-10-20-30-1002","fingerprint":"3333333333333333333333333333333333333333333333333333333333333333"},
              {"kind":"FolderAce","state":"Present","stableId":"ace-0000000000000000000000000001","fingerprint":"4444444444444444444444444444444444444444444444444444444444444444"},
              {"kind":"Share","state":"Present","stableId":"share-00000000000000000000000001","fingerprint":"5555555555555555555555555555555555555555555555555555555555555555"},
              {"kind":"FirewallRule","state":"Present","stableId":"rule-000000000000000000000000001","fingerprint":"6666666666666666666666666666666666666666666666666666666666666666"}
            ]
          }
        }
        """;
}
