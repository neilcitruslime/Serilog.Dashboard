using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Serilog.Dashboard.Api.Config;
using Serilog.Dashboard.Api.Models;

namespace Serilog.Dashboard.Api.Services
{
    public class ClickHouseLogService
    {
        private readonly ClickHouseOptions _options;
        private readonly HttpClient _httpClient;
        public ClickHouseLogService(IOptions<ClickHouseOptions> options)
        {
            _options = options.Value;
            _httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(_options.Username) && !string.IsNullOrEmpty(_options.Password))
            {
                var byteArray = System.Text.Encoding.ASCII.GetBytes($"{_options.Username}:{_options.Password}");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }
        }

        public async Task InsertLogsAsync(IEnumerable<SerilogEvent> events)
        {
            var insertSql = $"INSERT INTO {_options.Table} (ClientId, InstanceId, Timestamp, Level, Message, MessageTemplate, Properties, EventId) VALUES ";
            var values = new List<string>();
            foreach (var evt in events)
            {
                var props = evt.Properties != null ? string.Join(",", evt.Properties.Select(p => $"'{p.Key}':'{p.Value.ToString().Replace("'", "''")}'")) : "";
                var propsMap = $"{{{props}}}";
                values.Add($"({evt.ClientId}, {evt.InstanceId}, '{evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}', '{evt.Level?.Replace("'", "''")}', '{evt.Message?.Replace("'", "''")}', '{evt.MessageTemplate?.Replace("'", "''")}', {propsMap}, '{evt.EventId?.Replace("'", "''")}')");
            }
            var sql = insertSql + string.Join(",", values);
            var content = new StringContent(sql);
            var url = $"{_options.Host}/?database={_options.Database}";
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
    }
}
