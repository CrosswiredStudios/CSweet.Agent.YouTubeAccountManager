using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK;

namespace CSweet.Agent.YouTubeAccountManager.Tests;

public sealed class YouTubeAccountManagerAgentTests
{
    [Fact]
    public async Task DiscoverChannels_UsesOpaqueConnectionAndReturnsSafeChoices()
    {
        BrokeredHttpRequest? captured = null;
        var runtime = new AgentTestRuntime().RegisterCapability<BrokeredHttpRequest, BrokeredHttpResponse>(
            "web.fetch.v1", (request, _) =>
            {
                captured = request;
                return Task.FromResult(new BrokeredHttpResponse(200, request.Url, "application/json",
                    Encoding.UTF8.GetBytes("""{"items":[{"id":"UC123","snippet":{"title":"C-Sweet","customUrl":"@csweet","thumbnails":{"default":{"url":"https://example.test/avatar.png"}}}}]}"""), false));
            });

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.SetupDiscover, new { });

        Assert.True(result.Succeeded);
        Assert.Equal("google-youtube", captured?.Connection);
        Assert.Equal("UC123", result.Value!.Value.GetProperty("channels")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task PublicMutation_StopsAtApprovalWhenNotAuthorized()
    {
        var networkCalled = false;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ManagedActionRequest, ManagedActionResponse>("platform.managed-action.execute.v1",
                (_, _) => Task.FromResult(new ManagedActionResponse("Pending", "approval-1", false)))
            .RegisterCapability<BrokeredHttpRequest, BrokeredHttpResponse>("web.request.v1", (request, _) =>
            {
                networkCalled = true;
                return Task.FromResult(new BrokeredHttpResponse(200, request.Url, "application/json", [], false));
            });

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.EngagementManage,
            Operation("reply", JsonSerializer.SerializeToElement(new { textOriginal = "Thanks!" })));

        Assert.True(result.Succeeded);
        Assert.False(networkCalled);
        Assert.Equal("AwaitingApproval", result.Value!.Value.GetProperty("status").GetString());
        Assert.Equal("approval-1", result.Value.Value.GetProperty("approvalId").GetString());
    }

    [Fact]
    public async Task PermanentDeletion_IsAlwaysHardGated()
    {
        ManagedActionRequest? captured = null;
        var runtime = new AgentTestRuntime().RegisterCapability<ManagedActionRequest, ManagedActionResponse>(
            "platform.managed-action.execute.v1", (request, _) =>
            {
                captured = request;
                return Task.FromResult(new ManagedActionResponse("Pending", "hard-gate", false));
            });

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.VideoManage, Operation("delete-permanently", resourceId: "video-1"));

        Assert.True(result.Succeeded);
        Assert.True(captured?.AlwaysRequiresApproval);
    }

    [Fact]
    public async Task CaptionDeletion_IsAlwaysHardGated()
    {
        ManagedActionRequest? captured = null;
        var runtime = new AgentTestRuntime().RegisterCapability<ManagedActionRequest, ManagedActionResponse>(
            "platform.managed-action.execute.v1", (request, _) =>
            {
                captured = request;
                return Task.FromResult(new ManagedActionResponse("Pending", "hard-gate", false));
            });

        await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.VideoManage, Operation("caption-delete", resourceId: "caption-1"));

        Assert.True(captured?.AlwaysRequiresApproval);
    }

    [Fact]
    public async Task SetupValidation_ReportsEligibilityWithoutClaimingProgressiveFeatures()
    {
        var runtime = new AgentTestRuntime().RegisterCapability<BrokeredHttpRequest, BrokeredHttpResponse>(
            "web.fetch.v1", (request, _) => Task.FromResult(new BrokeredHttpResponse(200, request.Url,
                "application/json", Encoding.UTF8.GetBytes(
                    "{\"items\":[{\"id\":\"UC123\",\"status\":{\"longUploadsStatus\":\"allowed\"}}]}"), false)));

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.SetupValidate, new YouTubeSetupRequest("UC123"));

        Assert.True(result.Succeeded);
        var eligibility = result.Value!.Value.GetProperty("eligibility");
        Assert.Equal("Eligible", eligibility.GetProperty("longUploads").GetString());
        Assert.Contains("permission", eligibility.GetProperty("memberships").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommentSync_FlagsPotentialUrgencyAndRequestsBrandDraft()
    {
        JsonElement captured = default;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<BrokeredHttpRequest, BrokeredHttpResponse>("web.fetch.v1", (request, _) =>
                Task.FromResult(new BrokeredHttpResponse(200, request.Url, "application/json",
                    Encoding.UTF8.GetBytes("{\"items\":[{\"id\":\"comment-1\",\"snippet\":{\"topLevelComment\":{\"snippet\":{\"textOriginal\":\"Our attorney will file a lawsuit\"}}}}]}"), false)))
            .RegisterCapability<object, JsonElement>("platform.engagement-inbox.upsert.v1", (request, _) =>
            {
                captured = JsonSerializer.SerializeToElement(request);
                return Task.FromResult(JsonSerializer.SerializeToElement(new { persisted = true }));
            })
            .RegisterCapability<object, object>(PlatformCapabilities.BusinessProfileRead, (_, _) =>
                Task.FromResult<object>(new
                {
                    organizationId = Guid.NewGuid(), name = "C-Sweet", businessType = (string?)null,
                    industry = (string?)null, description = (string?)null, mission = (string?)null,
                    lifecycleStage = (string?)null, targetCustomers = Array.Empty<string>(),
                    offerings = Array.Empty<string>(), revenueModel = (string?)null,
                    jurisdictions = Array.Empty<string>(), operatingStyle = (string?)null,
                    constraints = Array.Empty<string>(), tools = Array.Empty<string>(),
                    riskPreference = (string?)null, timeZone = "UTC", revision = 1,
                    completeness = 1m, provenance = new Dictionary<string, object>()
                }));

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.EngagementManage, Operation("list-comments"));

        Assert.True(result.Succeeded);
        var item = captured.GetProperty("items")[0];
        Assert.True(item.GetProperty("urgent").GetBoolean());
        Assert.Equal("Prepared", item.GetProperty("draftStatus").GetString());
        Assert.Contains("C-Sweet", item.GetProperty("draftText").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_UsesBrokeredMediaTransferWithoutUploadUrlExposure()
    {
        MediaTransferRequest? captured = null;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ManagedActionRequest, ManagedActionResponse>("platform.managed-action.execute.v1",
                (_, _) => Task.FromResult(new ManagedActionResponse("Authorized", "approved", true)))
            .RegisterCapability<MediaTransferRequest, MediaTransferResponse>("platform.media.transfer.v1", (request, _) =>
            {
                captured = request;
                return Task.FromResult(new MediaTransferResponse("Completed", "video-42", JsonSerializer.SerializeToElement(new { id = "video-42" })));
            });
        var operation = Operation("publish") with { MediaAssetId = Guid.NewGuid() };

        var result = await runtime.ExecuteCapabilityAsync(new YouTubeAccountManagerAgent(),
            YouTubeAccountManagerAgent.VideoPublish, operation);

        Assert.True(result.Succeeded);
        Assert.Equal("google-youtube", captured?.Connection);
        Assert.Equal("UC123", captured?.BoundResourceId);
        Assert.Equal("video-42", result.Value!.Value.GetProperty("resourceId").GetString());
    }

    [Fact]
    public async Task Cancellation_IsHonored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentTestRuntime().ExecuteCapabilityAsync(
            new YouTubeAccountManagerAgent(), YouTubeAccountManagerAgent.AccountSync, Operation("sync"), cancellation.Token));
    }

    [Fact]
    public async Task Onboarding_RunsInitialCommentAndAnalyticsSynchronization()
    {
        var webCalls = 0;
        var engagementCalls = 0;
        var metricCalls = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<BrokeredHttpRequest, BrokeredHttpResponse>("web.fetch.v1", (request, _) =>
            {
                webCalls++;
                var body = request.Url.Contains("commentThreads", StringComparison.Ordinal)
                    ? "{\"items\":[]}" : "{\"rows\":[]}";
                return Task.FromResult(new BrokeredHttpResponse(200, request.Url, "application/json",
                    Encoding.UTF8.GetBytes(body), false));
            })
            .RegisterCapability<object, JsonElement>("platform.engagement-inbox.upsert.v1", (_, _) =>
            { engagementCalls++; return Task.FromResult(JsonSerializer.SerializeToElement(new { persisted = true })); })
            .RegisterCapability<object, JsonElement>("platform.metric-snapshot.write.v1", (_, _) =>
            { metricCalls++; return Task.FromResult(JsonSerializer.SerializeToElement(new { persisted = true })); })
            .RegisterCapability<object, JsonElement>("platform.synchronization-checkpoint.v1", (_, _) =>
                Task.FromResult(JsonSerializer.SerializeToElement(new { persisted = true })))
            .RegisterCapability<CompleteAgentOnboardingRequest, CompleteAgentOnboardingResponse>(
                AgentLifecycleCapabilities.CompleteOnboarding, (_, _) =>
                    Task.FromResult(new CompleteAgentOnboardingResponse(true, DateTimeOffset.UtcNow)));
        var agent = new YouTubeAccountManagerAgent();
        var settings = new Dictionary<string, JsonElement>
        {
            ["connectedChannelId"] = JsonSerializer.SerializeToElement("UC123")
        };
        var configured = await runtime.ExecuteCapabilityAsync(agent, AgentConfigurationCapabilities.Update,
            new UpdateAgentConfigurationRequest(settings));
        Assert.True(configured.Succeeded);

        await runtime.DeliverEventAsync(agent, AgentLifecycleEvents.Onboarded,
            new AgentOnboardedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal(2, webCalls);
        Assert.Equal(1, engagementCalls);
        Assert.Equal(1, metricCalls);
    }

    private static YouTubeOperationRequest Operation(string action, JsonElement? payload = null, string? resourceId = null) =>
        new("UC123", action, $"test:{Guid.NewGuid():N}", payload, resourceId);
}
