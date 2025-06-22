using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog.Dashboard.Api.Models;
using Serilog.Dashboard.Api.Services;
using Microsoft.Extensions.Options;
using Api.Interfaces;

[ApiController]
[Route("api/events/raw")]
public class RawEventsController : ControllerBase
{
    private readonly ILogStoreService logService;
    public RawEventsController(ILogStoreService logService)
    {
        this.logService = logService;
    }

    [HttpPost]
    public async Task<IActionResult> Post()
    {
        var request = HttpContext.Request;
        string rawBody;
        try
        {
            var contentType = request.ContentType ?? "";
            if (!request.ContentType?.Contains("application/vnd.serilog.clef") ?? true)
            {
                Console.WriteLine("⚠️ Unexpected content type.");
                return BadRequest("Unsupported content type.");
            }

            // Read and log the raw request body
            using (var reader = new StreamReader(request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            Console.WriteLine($"📦 Raw Body: {rawBody}");

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                Console.WriteLine("⚠️ Received empty request body.");
                return BadRequest("Request body is empty.");
            }

            var logEvents = ParseLogEvents(rawBody);
            if (logEvents.Count == 0)
            {
                Console.WriteLine("⚠️ No valid log events found.");
                return BadRequest("No valid log events found.");
            }

            await logService.InsertLogsAsync(logEvents);
            return NoContent();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❗ Error processing logs: {ex.Message}");
            return StatusCode(500);
        }
    }

    private List<SerilogEvent> ParseLogEvents(string rawBody)
    {
        var logEvents = new List<SerilogEvent>();
        try
        {
            var parsedArray = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(rawBody);
            if (parsedArray is not null)
            {
                   logEvents.AddRange(parsedArray.Select(evt => ParseSerilogEvent(evt)));
            }
             
        }
        catch
        {
            var lines = rawBody.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line.Trim());
                    if (parsed != null)
                        logEvents.Add(ParseSerilogEvent(parsed));
                }
                catch (JsonException je)
                {
                    Console.WriteLine($"⚠️ Skipping invalid line: {je.Message}");
                }
            }
        }
        return logEvents;
    }

    private SerilogEvent ParseSerilogEvent(Dictionary<string, JsonElement> evt)
    {
        evt.TryGetValue("@t", out var timestampElement);
        evt.TryGetValue("@l", out var levelElement);
        evt.TryGetValue("@m", out var messageElement);
        evt.TryGetValue("@mt", out var messageTemplateElement);
        evt.TryGetValue("@mx", out var exceptionElement);
        var properties = evt.Where(p => !p.Key.StartsWith("@") && p.Key != "MessageTemplate")
                            .ToDictionary(p => p.Key, p => p.Value);

        // Expand message template if present
        string? expandedMessage = null;
        if (messageTemplateElement.ValueKind == JsonValueKind.String)
        {
            var messageTemplate = messageTemplateElement.GetString() ?? string.Empty;
            expandedMessage = Regex.Replace(messageTemplate, @"{(?<token>[^}:]+)(:[^}]+)?}", match =>
            {
                var token = match.Groups["token"].Value;
                if (properties.TryGetValue(token, out var value))
                {
                    return value.ToString();
                }
                return match.Value; // Leave token as-is if not found
            });
        }

        return new SerilogEvent()
        {
            Timestamp = timestampElement.ValueKind == JsonValueKind.String && DateTime.TryParse(timestampElement.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts) ? ts : DateTime.UtcNow,
            Level = levelElement.ValueKind == JsonValueKind.String ? levelElement.GetString() : null,
            Message = messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : expandedMessage,
            MessageTemplate = messageTemplateElement.ValueKind == JsonValueKind.String ? messageTemplateElement.GetString() : null,
            Properties = properties.Count > 0 ? properties : null,
            ClientId = 0, // Placeholder, adjust as needed
            InstanceId = 0, // Placeholder, adjust as needed
            ExceptionInformation = exceptionElement.ValueKind == JsonValueKind.String ? exceptionElement.GetString() : null
        };
    }
}
