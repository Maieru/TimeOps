using System.Text.Json.Serialization;

namespace TimeOps.Infrastructure;

internal sealed record WiqlRequest(string Query);
internal sealed record WorkItemsRequest(int[] Ids,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Fields,
    string ErrorPolicy = "Fail",
    [property: JsonPropertyName("$expand"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Expand = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WiqlRequest))]
[JsonSerializable(typeof(WorkItemsRequest))]
internal partial class DevOpsRequestJsonContext : JsonSerializerContext;
