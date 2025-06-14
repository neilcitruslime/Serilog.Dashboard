using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog.Dashboard.Api.Models;

[ApiController]
[Route("api/events/raw")]
public class RawEventsController : ControllerBase
{
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

            LogEventsToConsole(logEvents);
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
                logEvents.AddRange(parsedArray.Select(ParseSerilogEvent));
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
        evt.TryGetValue("@t", out var tVal);
        evt.TryGetValue("@l", out var lVal);
        evt.TryGetValue("@m", out var mVal);
        evt.TryGetValue("@mt", out var mtVal);
        var properties = evt.Where(p => !p.Key.StartsWith("@") && p.Key != "MessageTemplate")
                            .ToDictionary(p => p.Key, p => p.Value);
        return new SerilogEvent()
        {
            Timestamp = tVal.ValueKind == JsonValueKind.String && DateTime.TryParse(tVal.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts) ? ts : DateTime.UtcNow,
            Level = lVal.ValueKind == JsonValueKind.String ? lVal.GetString() : null,
            Message = mVal.ValueKind == JsonValueKind.String ? mVal.GetString() : null,
            MessageTemplate = mtVal.ValueKind == JsonValueKind.String ? mtVal.GetString() : null,
            Properties = properties.Count > 0 ? properties : null,
            ClientId = 0, // Placeholder, adjust as needed
            InstanceId = 0, // Placeholder, adjust as needed
        };
    }

    private void LogEventsToConsole(List<SerilogEvent> logEvents)
    {
        foreach (var evt in logEvents)
        {
            var timestamp = evt.Timestamp?.ToString() ?? "n/a";
            var level = evt.Level ?? "Information";
            var template = !string.IsNullOrWhiteSpace(evt.Message) ? evt.Message :
                          !string.IsNullOrWhiteSpace(evt.MessageTemplate) ? evt.MessageTemplate :
                          "[No message]";
            var message = Regex.Replace(template, @"{(?<token>[^}:]+)(:[^}]+)?}", match =>
            {
                var token = match.Groups["token"].Value;
                if (evt.Properties != null && evt.Properties.TryGetValue(token, out var value))
                {
                    return value.ToString();
                }
                return match.Value; // Leave token as-is if not found
            });
            Console.WriteLine($"[{timestamp}] {level}: {message}");
            if (evt.Properties != null && evt.Properties.Count > 0)
            {
                Console.WriteLine("  • Properties:");
                foreach (var prop in evt.Properties)
                {
                    Console.WriteLine($"    - {prop.Key}: {prop.Value}");
                }
            }
        }
    }
}
