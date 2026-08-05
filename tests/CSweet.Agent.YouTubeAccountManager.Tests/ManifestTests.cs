using CSweet.Agent.SDK;

namespace CSweet.Agent.YouTubeAccountManager.Tests;

public sealed class ManifestTests
{
    [Fact]
    public async Task Manifest_IsValidAndUsesProgressivePermissions()
    {
        var manifest = await AgentManifestLoader.LoadAsync(Path.Combine(RepositoryRoot(), "csweet-plugin.json"), CancellationToken.None);
        Assert.Equal("com.csweet.youtube-account-manager", manifest.Id);
        Assert.Equal("AlwaysOn", manifest.Runtime.DefaultActivationMode);
        var connection = Assert.Single(manifest.Connections);
        Assert.Equal(["base", "publishing", "management", "memberships", "partner"], connection.ScopeSets.Select(x => x.Id));
        Assert.True(connection.ScopeSets.Single(x => x.Id == "base").Required);
        Assert.All(connection.ScopeSets.Where(x => x.Id != "base"), x => Assert.False(x.Required));
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(RepositoryRoot(), "csweet-plugin.json")), "clientSecret", StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "csweet-plugin.json"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
