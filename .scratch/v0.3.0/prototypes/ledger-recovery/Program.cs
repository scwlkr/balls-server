using BallsServer.LedgerRecovery;

LedgerRecoveryResult result = LedgerRecoveryPolicy.Evaluate(new(
    ProtectedCopyState.Corrupt,
    ProtectedCopyState.Corrupt,
    JournalValid: true,
    CopiesEquivalent: false,
    PrimaryRevision: null,
    MirrorRevision: null,
    CommittedRevision: 7,
    PrimaryHash: null,
    MirrorHash: null));

if (result is not
    {
        Disposition: LedgerRecoveryDisposition.TotalLossReadOnly,
        AutomationReadOnly: true,
        AuthorizesWindowsMutation: false,
    })
{
    Console.Error.WriteLine("FAIL: isolated ledger recovery model did not fail closed.");
    return 1;
}

Console.WriteLine("PASS: isolated ledger recovery model loaded; total loss is read-only; no mutation executed.");
return 0;
