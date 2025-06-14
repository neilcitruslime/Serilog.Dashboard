using System.Text.Json;

namespace Serilog.Dashboard.Api.Models
{
    public record SerilogEvent(
        DateTimeOffset? Timestamp,
        string? Level,
        string? Message,
        string? MessageTemplate,
        Dictionary<string, JsonElement>? Properties
    );
}
