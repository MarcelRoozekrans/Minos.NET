using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.OpenAI;

namespace Minos.Docs.Tests;

public static class OpenAIRegistration
{
    #region DependencyInjection_OpenAI
    // TypeSafe as the default client, and OpenAI beside it under the key "openai". Each key comes from configuration;
    // without one, the OpenAI client reads OPENAI_API_KEY, and the host fails at start-up when there is none.
    public static OpenAIDecisionClientServiceBuilder AddBothProviders(IHostApplicationBuilder builder)
    {
        builder.Services.AddSystemOneClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
        return builder.Services.AddOpenAIDecisionClient(
            "openai", options => options.ApiKey = builder.Configuration["OpenAI:ApiKey"]);
    }
    #endregion
}
