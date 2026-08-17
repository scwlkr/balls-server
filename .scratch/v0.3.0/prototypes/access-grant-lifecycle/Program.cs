using BallsServer.AccessGrantLifecycle;

AccessGrant grant = AccessGrant.Create(GrantFacts.ValidRequest());
if (grant.State != GrantState.PendingTransfer || !grant.Disabled)
{
    Console.Error.WriteLine("FAIL: isolated access-grant lifecycle did not fail closed.");
    return 1;
}

Console.WriteLine("PASS: isolated access-grant lifecycle model loaded; no system, network, account, credential, or filesystem mutation executed.");
return 0;
