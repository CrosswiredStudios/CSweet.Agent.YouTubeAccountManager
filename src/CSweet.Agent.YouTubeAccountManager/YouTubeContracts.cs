using System.Text.Json;

namespace CSweet.Agent.YouTubeAccountManager;

public sealed record YouTubeOperationRequest(
    string ChannelId,
    string Action,
    string IdempotencyKey,
    JsonElement? Payload = null,
    string? ResourceId = null,
    Guid? MediaAssetId = null,
    string? ApprovalId = null,
    long? ExpectedRevision = null);

public sealed record YouTubeOperationResponse(
    string Status,
    string Action,
    string ChannelId,
    string? ResourceId = null,
    JsonElement? Data = null,
    string? ApprovalId = null);

public sealed record YouTubeSetupRequest(string? ChannelId = null);
public sealed record YouTubeChannelOption(string Id, string Name, string? Handle, string? AvatarUrl);
public sealed record YouTubeFeatureEligibility(
    string StandardUploads,
    string LongUploads,
    string Live,
    string Memberships,
    string Partner);
public sealed record YouTubeSetupResponse(
    bool Healthy,
    IReadOnlyList<YouTubeChannelOption> Channels,
    string? Message = null,
    YouTubeFeatureEligibility? Eligibility = null);

public sealed record BrokeredHttpRequest(string Url, string Method = "GET",
    IReadOnlyDictionary<string, string>? Headers = null, string? Credential = null, byte[]? Body = null,
    string? ContentType = null, string? Connection = null, string? BoundResourceId = null);
public sealed record BrokeredHttpResponse(int StatusCode, string FinalUrl, string ContentType, byte[] Body, bool Truncated);
public sealed record ManagedActionRequest(string InstallationId, string ChannelId, string ActionType,
    JsonElement Payload, string PayloadHash, string IdempotencyKey, string? ApprovalId, long? ExpectedRevision,
    bool AlwaysRequiresApproval, string? ResourceId = null);
public sealed record ManagedActionResponse(string Status, string? ApprovalId, bool Authorized);
public sealed record MediaTransferRequest(Guid MediaAssetId, string Connection, string BoundResourceId,
    string InitiationUrl, JsonElement? Metadata, string IdempotencyKey);
public sealed record MediaTransferResponse(string Status, string? ExternalResourceId, JsonElement? Result);
