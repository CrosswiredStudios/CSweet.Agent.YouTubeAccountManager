using CSweet.Agent.SDK;
using CSweet.Agent.YouTubeAccountManager;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var agent = new YouTubeAccountManagerAgent();
    if (agent.AgentId != "com.csweet.youtube-account-manager" || agent.Version != "0.3.0")
        throw new InvalidOperationException("YouTube account manager identity self-test failed.");
    Console.WriteLine($"{agent.AgentId} {agent.Version} self-test passed.");
    return;
}
var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<YouTubeAccountManagerAgent>();
await builder.Build().RunAsync();
