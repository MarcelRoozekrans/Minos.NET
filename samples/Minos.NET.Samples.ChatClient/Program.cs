using Minos.Samples.ChatClient;

return await ChatClientApp.RunAsync(args, AppContext.BaseDirectory, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
