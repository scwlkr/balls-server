using BallsServer.SecurityPrototype;

if (args.Length == 2 && args[0] == "pipe-client")
{
    return await EphemeralNamedPipeProbe.RunClientAsync(args[1]);
}

Console.Error.WriteLine("This isolated prototype exposes only the bounded pipe-client feasibility mode.");
return 2;
