using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.YouTubeAccountManager;

public sealed class YouTubeAccountManagerAgent : CSweetAgentBase, IAgentConnectedService
{
    private BusinessProfileResponse? _brandProfile;
    public const string AccountSync = "youtube.account.sync.v1";
    public const string VideoPublish = "youtube.video.publish.v1";
    public const string VideoManage = "youtube.video.manage.v1";
    public const string EngagementManage = "youtube.engagement.manage.v1";
    public const string LiveManage = "youtube.live.manage.v1";
    public const string PartnerManage = "youtube.partner.manage.v1";
    public const string AnalyticsReport = "youtube.analytics.report.v1";
    public const string SetupDiscover = "youtube.setup.channels.v1";
    public const string SetupValidate = "youtube.setup.validate.v1";
    public const string ConfigurationDescribe = "agent.configuration.describe.v1";
    public const string ConfigurationUpdate = "agent.configuration.update.v1";
    public const string AssistantRespond = "assistant.converse.v1";
    public const string CheckIn = "management.check-in.v1";

    private const string WebFetch = "web.fetch.v1";
    private const string WebRequest = "web.request.v1";
    private const string ManagedAction = "platform.managed-action.execute.v1";
    private const string MediaTransfer = "platform.media.transfer.v1";
    private const string EngagementInbox = "platform.engagement-inbox.upsert.v1";
    private const string MetricSnapshot = "platform.metric-snapshot.write.v1";
    private const string SyncCheckpoint = "platform.synchronization-checkpoint.v1";
    private const string Connection = "google-youtube";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override string AgentId => "com.csweet.youtube-account-manager";
    public override string Version => "0.3.0";

    public override Task<PersonalTodoResult> HandlePersonalTodoAsync(
        PersonalTodoItem item, AgentRuntimeContext context, CancellationToken cancellationToken) =>
        Task.FromResult(PersonalTodoResult.Blocked(
            "YouTube operations require a typed channel operation and the existing approval policy; free-form personal queue requests are unsupported."));

    protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) => builder
        .Text("connectedChannelId", "Connected YouTube channel", false)
        .Select("approvalMode", "Approval mode",
            [new("Manager Approval", "Manager Approval"), new("CEO Approval", "CEO Approval"),
             new("Fully Autonomous", "Fully Autonomous within an owner-approved policy")],
            true, defaultValue: "Manager Approval")
        .Select("defaultPrivacy", "Default video privacy",
            [new("private", "Private"), new("unlisted", "Unlisted"), new("public", "Public")],
            true, defaultValue: "private")
        .Number("commentCadenceMinutes", "Comment sync cadence (minutes)", true,
            minimum: 15, maximum: 1440, step: 15, defaultValue: 15)
        .Select("reportingCadence", "Reporting cadence", [new("weekly", "Weekly")],
            true, defaultValue: "weekly");

    public override async Task HandleEventAsync(AgentEventEnvelope message, AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (message.EventType != AgentLifecycleEvents.Onboarded) return;
        var channelId = Settings.GetString("connectedChannelId");
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            await SynchronizeCommentsAsync(context, channelId, cancellationToken);
            await SnapshotAnalyticsAsync(context, channelId, cancellationToken);
        }
        await context.Platform.InvokeAsync<object, JsonElement>(SyncCheckpoint,
            new { operation = "initialize", source = "youtube", channelId, eventId = message.EventId,
                completedAt = DateTimeOffset.UtcNow }, cancellationToken);
        await context.Platform.Lifecycle.CompleteOnboardingAsync(message, cancellationToken);
    }

    protected override async Task<AgentWorkResult> ExecuteCapabilityCoreAsync(AgentCapabilityRequest request,
        AgentRuntimeContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return request.Capability switch
            {
                SetupDiscover => AgentWorkResult.Success(await DiscoverChannelsAsync(context, cancellationToken)),
                SetupValidate => AgentWorkResult.Success(await ValidateSetupAsync(request, context, cancellationToken)),
                AccountSync => AgentWorkResult.Success(await ExecuteOperationAsync(request, context, false, cancellationToken)),
                AnalyticsReport => AgentWorkResult.Success(await ExecuteOperationAsync(request, context, false, cancellationToken)),
                VideoPublish or VideoManage or EngagementManage or LiveManage or PartnerManage =>
                    AgentWorkResult.Success(await ExecuteOperationAsync(request, context, true, cancellationToken)),
                AssistantRespond => AgentWorkResult.Success(new { message = "I can synchronize your channel, prepare and publish content, manage engagement, coordinate live operations, and report official YouTube metrics." }),
                CheckIn => AgentWorkResult.Success(new { healthy = true, checkedAt = DateTimeOffset.UtcNow }),
                _ => AgentWorkResult.Failure($"Capability '{request.Capability}' is not supported.")
            };
        }
        catch (JsonException) { return AgentWorkResult.Failure("The request payload is not valid."); }
        catch (PlatformCapabilityException exception) { return AgentWorkResult.Failure($"Platform operation failed: {exception.Message}"); }
        catch (InvalidOperationException exception) { return AgentWorkResult.Failure(exception.Message); }
    }

    private static async Task<YouTubeSetupResponse> DiscoverChannelsAsync(AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var response = await context.Platform.InvokeAsync<BrokeredHttpRequest, BrokeredHttpResponse>(WebFetch,
            new("https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", Connection: Connection), cancellationToken);
        EnsureSuccess(response);
        using var json = JsonDocument.Parse(response.Body);
        var channels = json.RootElement.TryGetProperty("items", out var items)
            ? items.EnumerateArray().Select(item =>
            {
                var snippet = item.GetProperty("snippet");
                return new YouTubeChannelOption(item.GetProperty("id").GetString()!,
                    snippet.GetProperty("title").GetString()!,
                    snippet.TryGetProperty("customUrl", out var handle) ? handle.GetString() : null,
                    snippet.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.TryGetProperty("default", out var avatar)
                        ? avatar.GetProperty("url").GetString() : null);
            }).ToArray()
            : [];
        return new(channels.Length > 0, channels, channels.Length > 0 ? null : "Google returned no accessible YouTube channels.");
    }

    private static async Task<YouTubeSetupResponse> ValidateSetupAsync(AgentCapabilityRequest request,
        AgentRuntimeContext context, CancellationToken cancellationToken)
    {
        var input = DeserializePayload<YouTubeSetupRequest>(request.Arguments);
        if (string.IsNullOrWhiteSpace(input?.ChannelId)) throw new InvalidOperationException("channelId is required.");
        var response = await context.Platform.InvokeAsync<BrokeredHttpRequest, BrokeredHttpResponse>(WebFetch,
            new($"https://www.googleapis.com/youtube/v3/channels?part=snippet,status,contentDetails,statistics&id={Uri.EscapeDataString(input.ChannelId)}",
                Connection: Connection, BoundResourceId: input.ChannelId), cancellationToken);
        EnsureSuccess(response);
        using var document = JsonDocument.Parse(response.Body);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() != 1 || items[0].GetProperty("id").GetString() != input.ChannelId)
            throw new InvalidOperationException("Google did not return the confirmed channel for this connection.");
        var channel = items[0];
        var longUploadsStatus = channel.TryGetProperty("status", out var status) &&
            status.TryGetProperty("longUploadsStatus", out var longUploads) ? longUploads.GetString() : null;
        var eligibility = new YouTubeFeatureEligibility(
            "Eligible",
            longUploadsStatus switch
            {
                "allowed" => "Eligible",
                "eligible" => "Requires channel verification",
                "disallowed" => "Not eligible",
                _ => "Eligibility not reported"
            },
            "Checked after management permission is enabled",
            "Checked after memberships permission is enabled",
            "Checked after partner permission is enabled");
        return new(true, [], "The connected channel and base permissions are healthy.", eligibility);
    }

    private async Task<YouTubeOperationResponse> ExecuteOperationAsync(AgentCapabilityRequest request,
        AgentRuntimeContext context, bool mutation, CancellationToken cancellationToken)
    {
        var input = DeserializePayload<YouTubeOperationRequest>(request.Arguments)
            ?? throw new InvalidOperationException("The operation payload is required.");
        Validate(input);
        await context.ReportProgressAsync(new { stage = "validated", input.Action }, cancellationToken);

        if (mutation && !IsReadAction(input.Action))
        {
            var payload = input.Payload ?? JsonSerializer.SerializeToElement(new { });
            var alwaysApproval = input.Action is "delete-permanently" or "playlist-delete" or "caption-delete" or
                "ban-user" or "go-live" or "content-id-claim" or "content-id-policy" or
                "ownership-change" or "monetization-change" or "ad-change";
            var decision = await context.Platform.InvokeAsync<ManagedActionRequest, ManagedActionResponse>(ManagedAction,
                new(context.InstallationId, input.ChannelId, input.Action, payload, Hash(payload), input.IdempotencyKey,
                    input.ApprovalId, input.ExpectedRevision, alwaysApproval, input.ResourceId), cancellationToken);
            if (!decision.Authorized)
                return new("AwaitingApproval", input.Action, input.ChannelId, input.ResourceId, ApprovalId: decision.ApprovalId);
        }

        if (input.MediaAssetId.HasValue)
        {
            var initiationUrl = input.Action switch
            {
                "set-thumbnail" when !string.IsNullOrWhiteSpace(input.ResourceId) =>
                    $"https://www.googleapis.com/upload/youtube/v3/thumbnails/set?uploadType=resumable&videoId={Uri.EscapeDataString(input.ResourceId)}",
                "caption-upload" => "https://www.googleapis.com/upload/youtube/v3/captions?uploadType=resumable&part=snippet",
                _ => "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status"
            };
            var transfer = await context.Platform.InvokeAsync<MediaTransferRequest, MediaTransferResponse>(MediaTransfer,
                new(input.MediaAssetId.Value, Connection, input.ChannelId,
                    initiationUrl,
                    input.Payload, input.IdempotencyKey), cancellationToken);
            return new(transfer.Status, input.Action, input.ChannelId, transfer.ExternalResourceId, transfer.Result);
        }

        var (url, method) = Route(input, request.Capability);
        var body = input.Payload.HasValue ? JsonSerializer.SerializeToUtf8Bytes(input.Payload.Value, JsonOptions) : null;
        var platformCapability = method == "GET" ? WebFetch : WebRequest;
        var response = await context.Platform.InvokeAsync<BrokeredHttpRequest, BrokeredHttpResponse>(platformCapability,
            new(url, method, Body: body, ContentType: body is null ? null : "application/json", Connection: Connection,
                BoundResourceId: input.ChannelId), cancellationToken);
        EnsureSuccess(response);
        var result = response.Body.Length == 0 ? (JsonElement?)null : JsonDocument.Parse(response.Body).RootElement.Clone();

        if (request.Capability == EngagementManage && method == "GET" && result.HasValue)
        {
            var brand = await GetBrandProfileAsync(context, cancellationToken);
            var items = result.Value.TryGetProperty("items", out var responseItems) && responseItems.ValueKind == JsonValueKind.Array
                ? responseItems.EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x =>
                {
                    var excerpt = FindCommentText(x) ?? "YouTube comment";
                    return new
                    {
                        externalId = x.GetProperty("id").GetString(), payload = x.Clone(),
                        observedAt = DateTimeOffset.UtcNow, urgent = IsPotentiallyUrgent(excerpt), excerpt,
                        draftStatus = "Prepared", draftText = PrepareReplyDraft(excerpt, brand)
                    };
                }).ToArray()
                : [];
            await context.Platform.InvokeAsync<object, JsonElement>(EngagementInbox, new
            {
                channelId = input.ChannelId, source = "youtube", items
            }, cancellationToken);
        }
        if (request.Capability == AnalyticsReport && result.HasValue)
            await context.Platform.InvokeAsync<object, JsonElement>(MetricSnapshot, new
            {
                channelId = input.ChannelId, source = "youtube-official", metrics = result.Value
            }, cancellationToken);
        return new("Completed", input.Action, input.ChannelId, input.ResourceId, result);
    }

    private static (string Url, string Method) Route(YouTubeOperationRequest input, string capability)
    {
        var id = Uri.EscapeDataString(input.ResourceId ?? string.Empty);
        return (capability, input.Action) switch
        {
            (AccountSync, _) => ($"https://www.googleapis.com/youtube/v3/channels?part=snippet,contentDetails,statistics&id={Uri.EscapeDataString(input.ChannelId)}", "GET"),
            (AnalyticsReport, _) => ($"https://youtubeanalytics.googleapis.com/v2/reports?ids=channel%3D%3D{Uri.EscapeDataString(input.ChannelId)}&startDate={DateTime.UtcNow.AddDays(-7):yyyy-MM-dd}&endDate={DateTime.UtcNow:yyyy-MM-dd}&metrics=views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,likes,comments,shares,subscribersGained,subscribersLost", "GET"),
            (VideoManage, "list") => ("https://www.googleapis.com/youtube/v3/videos?part=snippet,status&mine=true", "GET"),
            (VideoManage, "update") => ("https://www.googleapis.com/youtube/v3/videos?part=snippet,status", "PUT"),
            (VideoManage, "delete-permanently") => ($"https://www.googleapis.com/youtube/v3/videos?id={id}", "DELETE"),
            (VideoManage, "playlist-list") => ("https://www.googleapis.com/youtube/v3/playlists?part=snippet,status&mine=true", "GET"),
            (VideoManage, "playlist-create") => ("https://www.googleapis.com/youtube/v3/playlists?part=snippet,status", "POST"),
            (VideoManage, "playlist-update") => ("https://www.googleapis.com/youtube/v3/playlists?part=snippet,status", "PUT"),
            (VideoManage, "playlist-delete") => ($"https://www.googleapis.com/youtube/v3/playlists?id={id}", "DELETE"),
            (VideoManage, "playlist-item-add") => ("https://www.googleapis.com/youtube/v3/playlistItems?part=snippet,contentDetails", "POST"),
            (VideoManage, "playlist-item-remove") => ($"https://www.googleapis.com/youtube/v3/playlistItems?id={id}", "DELETE"),
            (VideoManage, "caption-list") => ($"https://www.googleapis.com/youtube/v3/captions?part=snippet&videoId={id}", "GET"),
            (VideoManage, "caption-update") => ("https://www.googleapis.com/youtube/v3/captions?part=snippet", "PUT"),
            (VideoManage, "caption-delete") => ($"https://www.googleapis.com/youtube/v3/captions?id={id}", "DELETE"),
            (EngagementManage, "list-comments") => ($"https://www.googleapis.com/youtube/v3/commentThreads?part=snippet,replies&allThreadsRelatedToChannelId={Uri.EscapeDataString(input.ChannelId)}&maxResults=100", "GET"),
            (EngagementManage, "reply") => ("https://www.googleapis.com/youtube/v3/comments?part=snippet", "POST"),
            (EngagementManage, "moderate") => ("https://www.googleapis.com/youtube/v3/comments/setModerationStatus", "POST"),
            (LiveManage, "list") => ("https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet,status,contentDetails&broadcastStatus=all", "GET"),
            (LiveManage, "list-streams") => ("https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,status,cdn&mine=true", "GET"),
            (LiveManage, "create-stream") => ("https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,cdn,contentDetails", "POST"),
            (LiveManage, "update-stream") => ("https://www.googleapis.com/youtube/v3/liveStreams?part=snippet,cdn,contentDetails", "PUT"),
            (LiveManage, "bind") => ($"https://www.googleapis.com/youtube/v3/liveBroadcasts/bind?id={id}", "POST"),
            (LiveManage, "go-live") => ($"https://www.googleapis.com/youtube/v3/liveBroadcasts/transition?broadcastStatus=live&id={id}&part=status", "POST"),
            (LiveManage, "chat-list") => ($"https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId={id}&part=snippet,authorDetails", "GET"),
            (LiveManage, "chat-send") => ("https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet", "POST"),
            (LiveManage, "update") => ("https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet,status,contentDetails", "PUT"),
            (LiveManage, _) => ("https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet,status,contentDetails", "POST"),
            (PartnerManage, "members-list") => ("https://www.googleapis.com/youtube/v3/members?part=snippet&mode=all_current", "GET"),
            (PartnerManage, _) => ("https://www.googleapis.com/youtube/partner/v1/assets", "POST"),
            _ => throw new InvalidOperationException($"Action '{input.Action}' is not supported by {capability}.")
        };
    }

    private static void Validate(YouTubeOperationRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.ChannelId) || input.ChannelId.Length > 256) throw new InvalidOperationException("channelId is required.");
        if (string.IsNullOrWhiteSpace(input.Action) || input.Action.Length > 80) throw new InvalidOperationException("action is required.");
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length > 160) throw new InvalidOperationException("idempotencyKey is required.");
    }
    private static bool IsReadAction(string action) => action is "list" or "list-comments" or "playlist-list" or
        "caption-list" or "list-streams" or "chat-list" or "members-list" or "sync" or "report";
    private static void EnsureSuccess(BrokeredHttpResponse response)
    {
        if (response.StatusCode is < 200 or >= 300) throw new InvalidOperationException($"YouTube returned status {response.StatusCode}.");
        if (response.Truncated) throw new InvalidOperationException("YouTube returned more data than the safe response limit.");
    }
    private static string Hash(JsonElement payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.GetRawText()))).ToLowerInvariant();
    protected override string? ValidateConfigurationUpdate(AgentConfigurationField field, JsonElement value,
        AgentSettings currentSettings)
    {
        if (field.Key == "connectedChannelId" && value.ValueKind == JsonValueKind.String &&
            value.GetString()?.Length > 256) return "The connected channel ID is invalid.";
        return null;
    }

    public async Task RunConnectedAsync(AgentRuntimeContext context, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        var lastReport = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var channelId = Settings.GetString("connectedChannelId");
                if (string.IsNullOrWhiteSpace(channelId)) continue;
                var engagement = await SynchronizeCommentsAsync(context, channelId, cancellationToken);
                if (DateTimeOffset.UtcNow - engagement.LastDigestAt >= TimeSpan.FromDays(1))
                {
                    await context.Platform.InvokeAsync<object, JsonElement>(EngagementInbox,
                        new { channelId, source = "youtube", items = Array.Empty<object>(),
                            digest = new { total = engagement.Total, urgent = engagement.Urgent } }, cancellationToken);
                    _lastDigestAt = DateTimeOffset.UtcNow;
                }
                if (DateTimeOffset.UtcNow - lastReport >= TimeSpan.FromDays(7))
                {
                    await SnapshotAnalyticsAsync(context, channelId, cancellationToken);
                    lastReport = DateTimeOffset.UtcNow;
                }
                await context.Platform.InvokeAsync<object, JsonElement>(SyncCheckpoint,
                    new { operation = "completed", source = "youtube-comments", channelId, cadenceMinutes = 15,
                        completedAt = DateTimeOffset.UtcNow }, cancellationToken);
            }
            catch (Exception exception) when (exception is PlatformCapabilityException or InvalidOperationException or JsonException)
            { /* provider access or grants can change while connected; fail closed until the next tick */ }
        }
    }

    private DateTimeOffset _lastDigestAt = DateTimeOffset.MinValue;

    private async Task<CommentSyncSummary> SynchronizeCommentsAsync(AgentRuntimeContext context, string channelId,
        CancellationToken cancellationToken)
    {
        var response = await context.Platform.InvokeAsync<BrokeredHttpRequest, BrokeredHttpResponse>(WebFetch,
            new($"https://www.googleapis.com/youtube/v3/commentThreads?part=snippet,replies&allThreadsRelatedToChannelId={Uri.EscapeDataString(channelId)}&maxResults=100",
                Connection: Connection, BoundResourceId: channelId), cancellationToken);
        EnsureSuccess(response);
        using var document = JsonDocument.Parse(response.Body);
        var brand = await GetBrandProfileAsync(context, cancellationToken);
        var items = document.RootElement.TryGetProperty("items", out var comments) && comments.ValueKind == JsonValueKind.Array
            ? comments.EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x =>
            {
                var excerpt = FindCommentText(x) ?? "YouTube comment";
                return new
                {
                    externalId = x.GetProperty("id").GetString(), payload = x.Clone(),
                    observedAt = DateTimeOffset.UtcNow, urgent = IsPotentiallyUrgent(excerpt), excerpt,
                    draftStatus = "Prepared", draftText = PrepareReplyDraft(excerpt, brand)
                };
            }).ToArray()
            : [];
        await context.Platform.InvokeAsync<object, JsonElement>(EngagementInbox,
            new { channelId, source = "youtube", items }, cancellationToken);
        var summary = new CommentSyncSummary(items.Length, items.Count(x => x.urgent), _lastDigestAt);
        return summary;
    }

    private static string? FindCommentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "textDisplay" or "textOriginal" && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var nested = FindCommentText(property.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindCommentText(item);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        return null;
    }

    private static bool IsPotentiallyUrgent(string value)
    {
        string[] indicators = ["legal action", "lawsuit", "attorney", "kill", "suicide", "emergency", "reporter", "press inquiry"];
        return indicators.Any(indicator => value.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<BusinessProfileResponse?> GetBrandProfileAsync(AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (_brandProfile is not null) return _brandProfile;
        try { return _brandProfile = await context.Platform.ReadBusinessProfileAsync(cancellationToken); }
        catch (PlatformCapabilityException) { return null; }
    }

    private static string PrepareReplyDraft(string comment, BusinessProfileResponse? brand)
    {
        var name = string.IsNullOrWhiteSpace(brand?.Name) ? "our team" : brand.Name;
        if (IsPotentiallyUrgent(comment))
            return $"Thank you for bringing this to {name}'s attention. We've escalated it for prompt human review and will respond through the appropriate channel.";
        if (comment.Contains('?'))
            return $"Thanks for your question. The {name} team appreciates your interest and will follow up with accurate details.";
        return $"Thanks for sharing this with {name}. We appreciate you taking the time to comment.";
    }

    private sealed record CommentSyncSummary(int Total, int Urgent, DateTimeOffset LastDigestAt);

    private static async Task SnapshotAnalyticsAsync(AgentRuntimeContext context, string channelId,
        CancellationToken cancellationToken)
    {
        var response = await context.Platform.InvokeAsync<BrokeredHttpRequest, BrokeredHttpResponse>(WebFetch,
            new($"https://youtubeanalytics.googleapis.com/v2/reports?ids=channel%3D%3D{Uri.EscapeDataString(channelId)}&startDate={DateTime.UtcNow.AddDays(-7):yyyy-MM-dd}&endDate={DateTime.UtcNow:yyyy-MM-dd}&metrics=views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,likes,comments,shares,subscribersGained,subscribersLost",
                Connection: Connection, BoundResourceId: channelId), cancellationToken);
        EnsureSuccess(response);
        using var document = JsonDocument.Parse(response.Body);
        await context.Platform.InvokeAsync<object, JsonElement>(MetricSnapshot,
            new { channelId, source = "youtube-official", capturedAt = DateTimeOffset.UtcNow,
                metrics = document.RootElement.Clone() }, cancellationToken);
    }
}
