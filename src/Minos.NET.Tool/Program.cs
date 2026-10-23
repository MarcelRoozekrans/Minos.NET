using Minos.Tool;

using var signals = new StopSignals();
return await MinosCli.RunAsync(args, Console.Out, Console.Error, network: null, signals.Token).ConfigureAwait(false);
