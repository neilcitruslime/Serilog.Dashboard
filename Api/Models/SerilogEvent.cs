using System.Text.Json;

namespace Serilog.Dashboard.Api.Models;

public class SerilogEvent
{
    public required long ClientId;
    public required long InstanceId;
    public required DateTime Timestamp;
    public string? Level;
    public string? Message;
    public string? MessageTemplate;
    public Dictionary<string, JsonElement>? Properties;
    public string EventId = Guid.NewGuid().ToString();
    public string? ExceptionInformation { get; internal set; }
}
