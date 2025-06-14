namespace Serilog.Dashboard.Api.Config
{
    public class ClickHouseOptions
    {
        public string Host { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Table { get; set; } = "logs";
    }
}
