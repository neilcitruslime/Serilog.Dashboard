using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog.Dashboard.Api.Config;
using Serilog.Dashboard.Api.Models;
using Api.Interfaces;

namespace Serilog.Dashboard.Api.Services
{
    public class PostgresLogService : ILogStoreService
    {
        private readonly PostgresOptions _options;
        private readonly ILogger<PostgresLogService> _logger;
        private const string TableName = "serilog_events";
        private const string SchemaName = "public";
        
        public PostgresLogService(IOptions<PostgresOptions> options, ILogger<PostgresLogService> logger)
        {
            _options = options.Value;
            _logger = logger;
            EnsureTableExists().GetAwaiter().GetResult();
        }

        private async Task EnsureTableExists()
        {
            try
            {
                using var connection = new NpgsqlConnection(_options.ConnectionString);
                await connection.OpenAsync();
                
                var tableExistsQuery = @$"
                    SELECT EXISTS (
                        SELECT FROM information_schema.tables 
                        WHERE table_schema = '{SchemaName}'
                        AND table_name = '{TableName}'
                    );";
                    
                var tableExists = await connection.ExecuteScalarAsync<bool>(tableExistsQuery);
                
                if (!tableExists)
                {
                    _logger.LogInformation("Creating log table {Table} in schema {Schema}", TableName, SchemaName);
                    
                    var createTableSql = $@"
                        CREATE TABLE {SchemaName}.{TableName} (
                            id SERIAL PRIMARY KEY,
                            client_id BIGINT NOT NULL,
                            instance_id BIGINT NOT NULL,
                            timestamp TIMESTAMP NOT NULL,
                            level VARCHAR(50),
                            message TEXT,
                            message_template TEXT,
                            properties JSONB,
                            event_id VARCHAR(50) NOT NULL,
                            exception_information TEXT,
                            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                        );
                        
                        CREATE INDEX idx_{TableName}_timestamp ON {SchemaName}.{TableName} (timestamp);
                        CREATE INDEX idx_{TableName}_level ON {SchemaName}.{TableName} (level);
                        CREATE INDEX idx_{TableName}_client_instance ON {SchemaName}.{TableName} (client_id, instance_id);
                    ";
                    
                    await connection.ExecuteAsync(createTableSql);
                    _logger.LogInformation("Log table created successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure log table exists");
                throw;
            }
        }

        public async Task InsertLogsAsync(IEnumerable<SerilogEvent> events)
        {
            try
            {
                using var connection = new NpgsqlConnection(_options.ConnectionString);
                await connection.OpenAsync();
                
                var insertSql = $@"
                    INSERT INTO {SchemaName}.{TableName} (
                        client_id, instance_id, timestamp, level, message, 
                        message_template, properties, event_id, exception_information
                    )
                    VALUES (
                        @ClientId, @InstanceId, @Timestamp, @Level, @Message, 
                        @MessageTemplate, @Properties::jsonb, @EventId, @ExceptionInfo
                    )";

                var eventList = events.ToList();
                _logger.LogInformation("Inserting {Count} log events into PostgreSQL", eventList.Count);

                foreach (var batch in BatchEvents(eventList, 50))
                {
                    var parameters = batch.Select(evt => new
                    {
                        ClientId = evt.ClientId,
                        InstanceId = evt.InstanceId,
                        Timestamp = evt.Timestamp,
                        Level = evt.Level,
                        Message = evt.Message,
                        MessageTemplate = evt.MessageTemplate,
                        Properties = evt.Properties != null ? JsonSerializer.Serialize(evt.Properties) : "{}",
                        EventId = evt.EventId,
                        ExceptionInfo = evt.ExceptionInformation,
                    }).ToList();

                    await connection.ExecuteAsync(insertSql, parameters);
                }
                
                _logger.LogInformation("Successfully inserted {Count} log events", eventList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert log events into PostgreSQL");
                throw;
            }
        }

        private IEnumerable<List<T>> BatchEvents<T>(List<T> source, int batchSize)
        {
            for (int i = 0; i < source.Count; i += batchSize)
            {
                yield return source.GetRange(i, Math.Min(batchSize, source.Count - i));
            }
        }
    }
}