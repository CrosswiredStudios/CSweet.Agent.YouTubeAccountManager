using CSweet.Agent.SDK;
using CSweet.Agent.YouTubeAccountManager;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<YouTubeAccountManagerAgent>();
await builder.Build().RunAsync();
