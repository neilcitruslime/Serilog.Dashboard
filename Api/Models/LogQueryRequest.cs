using System.Text.Json;

namespace Serilog.Dashboard.Api.Models
{
    public class LogQueryRequest
    {
        public required long ClientId { get; set; }
        public required long InstanceId { get; set; }
        public List<LogQueryCondition> Conditions { get; set; } = new List<LogQueryCondition>();
        public string? TimeZone { get; set; } = "UTC";
        public int PageSize { get; set; } = 100;
        public int PageNumber { get; set; } = 1;
    }

    public class LogQueryCondition
    {
        public string Field { get; set; } = string.Empty;
        public string Operator { get; set; } = "=";
        public string Value { get; set; } = string.Empty;

        public string ToSql(int paramIndex, out object paramValue, out bool isJsonProperty)
        {
            paramValue = Value;
            var paramName = $"@p{paramIndex}";
            
            // List of regular table columns
            var tableColumns = new[] { "client_id", "instance_id", "timestamp", "level", "message", 
                                      "message_template", "event_id", "exception_information", "raw" };
            
            // Check if the field is a regular column or a property in the JSON
            isJsonProperty = !tableColumns.Contains(Field.ToLower());
            
            // Handle special cases for operators that don't use parameter values
            if (Operator.ToUpper() == "IS NULL")
            {
                return isJsonProperty 
                    ? $"properties->'{Field}' IS NULL" 
                    : $"{Field} IS NULL";
            }
            
            if (Operator.ToUpper() == "IS NOT NULL")
            {
                return isJsonProperty 
                    ? $"properties->'{Field}' IS NOT NULL" 
                    : $"{Field} IS NOT NULL";
            }

            // Handle special case for timestamp with timezone conversion
            if (Field.ToLower() == "timestamp" && !isJsonProperty)
            {
                paramValue = DateTime.Parse(Value);
                return $"{Field} {Operator} {paramName}";
            }

            // Handle JSON property conditions
            if (isJsonProperty)
            {
                // For text comparison in JSONB fields
                if (Operator.ToUpper() == "LIKE" || Operator.ToUpper() == "NOT LIKE")
                {
                    return $"properties->'{Field}'->>'value' {Operator} {paramName}";
                }
                
                // For numeric comparison in JSONB fields (PostgreSQL needs explicit casting)
                if (double.TryParse(Value, out _))
                {
                    return $"(properties->'{Field}'->>'value')::numeric {Operator} {paramName}::numeric";
                }
                
                // For boolean values
                if (bool.TryParse(Value, out _))
                {
                    return $"(properties->'{Field}'->>'value')::boolean {Operator} {paramName}::boolean";
                }
                
                // Default text comparison
                return $"properties->'{Field}'->>'value' {Operator} {paramName}";
            }

            // Default case for regular columns
            return $"{Field} {Operator} {paramName}";
        }
    }

    public class LogQueryResponse
    {
        public List<SerilogEvent> Events { get; set; } = new List<SerilogEvent>();
        public int TotalCount { get; set; }
        public int PageSize { get; set; }
        public int PageNumber { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}