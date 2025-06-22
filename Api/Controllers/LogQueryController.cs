using Microsoft.AspNetCore.Mvc;
using Serilog.Dashboard.Api.Models;
using System.Data;
using System.Globalization;
using Dapper;
using Npgsql;
using System.Text;
using Microsoft.Extensions.Options;
using Serilog.Dashboard.Api.Config;

namespace Serilog.Dashboard.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogQueryController : ControllerBase
    {
        private readonly ILogger<LogQueryController> _logger;
        private readonly PostgresOptions _options;
        private const string TableName = "serilog_events";
        private const string SchemaName = "public";

        public LogQueryController(
            ILogger<LogQueryController> logger,
            IOptions<PostgresOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        [HttpPost("search")]
        public async Task<ActionResult<LogQueryResponse>> SearchLogs([FromBody] LogQueryRequest request)
        {
            try
            {
                // Set up timezone info for conversion
                TimeZoneInfo timeZone = TimeZoneInfo.Utc;
                if (!string.IsNullOrEmpty(request.TimeZone))
                {
                    try
                    {
                        timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Invalid timezone {TimeZone} specified, using UTC", request.TimeZone);
                    }
                }

                // Build the query
                var queryBuilder = new StringBuilder();
                var countQueryBuilder = new StringBuilder();
                var parameters = new DynamicParameters();
                var whereClauses = new List<string>();
                
                // Add mandatory client_id and instance_id conditions
                whereClauses.Add("client_id = @ClientId");
                whereClauses.Add("instance_id = @InstanceId");
                parameters.Add("@ClientId", request.ClientId);
                parameters.Add("@InstanceId", request.InstanceId);
                
                // Process additional conditions
                int paramIndex = 0;
                foreach (var condition in request.Conditions)
                {
                    string whereClauseElement = condition.ToSql(paramIndex, out var paramValue, out var isJsonProperty);
                    whereClauses.Add(whereClauseElement);
                    parameters.Add($"@p{paramIndex}", paramValue);
                    paramIndex++;
                }

                // Build where clause
                string whereClause = $"WHERE {string.Join(" AND ", whereClauses)}";

                // Build the main query - always sort by timestamp descending (newest first)
                int offset = (request.PageNumber - 1) * request.PageSize;
                
                queryBuilder.Append($@"
                    SELECT 
                        client_id, instance_id, timestamp, level, 
                        message, message_template, properties, event_id, 
                        exception_information, raw
                    FROM {SchemaName}.{TableName}
                    {whereClause}
                    ORDER BY timestamp DESC
                    LIMIT @PageSize OFFSET @Offset
                ");
                
                // Build the count query
                countQueryBuilder.Append($@"
                    SELECT COUNT(*) 
                    FROM {SchemaName}.{TableName}
                    {whereClause}
                ");
                
                // Add pagination parameters
                parameters.Add("@PageSize", request.PageSize);
                parameters.Add("@Offset", offset);

                // Execute the query
                using var connection = new NpgsqlConnection(_options.ConnectionString);
                await connection.OpenAsync();
                
                _logger.LogInformation("Executing log search query for client {ClientId}, instance {InstanceId}", 
                    request.ClientId, request.InstanceId);
                
                var events = (await connection.QueryAsync<SerilogEvent>(queryBuilder.ToString(), parameters)).ToList();
                var totalCount = await connection.ExecuteScalarAsync<int>(countQueryBuilder.ToString(), parameters);
                
                // Convert timestamps to requested timezone if needed
                if (timeZone.Id != "UTC")
                {
                    foreach (var evt in events)
                    {
                        evt.Timestamp = TimeZoneInfo.ConvertTimeFromUtc(evt.Timestamp.ToUniversalTime(), timeZone);
                    }
                }

                // Return the response
                var response = new LogQueryResponse
                {
                    Events = events,
                    TotalCount = totalCount,
                    PageSize = request.PageSize,
                    PageNumber = request.PageNumber
                };
                
                _logger.LogInformation("Log search returned {Count} results from {TotalCount} total matching records", 
                    events.Count, totalCount);
                
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing log search");
                return StatusCode(500, $"An error occurred while searching logs: {ex.Message}");
            }
        }
    }
}