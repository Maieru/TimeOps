using System.Text.Json.Serialization;

namespace TimeOps.Infrastructure;

internal sealed record WiqlRequest(string Query);
internal sealed record WorkItemsRequest(int[] Ids, string[] Fields, string ErrorPolicy = "Fail");

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WiqlRequest))]
[JsonSerializable(typeof(WorkItemsRequest))]
internal partial class DevOpsRequestJsonContext : JsonSerializerContext;
