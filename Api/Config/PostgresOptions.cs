namespace Serilog.Dashboard.Api.Config
{
    public class PostgresOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "logs";
        public string Username { get; set; } = "devuser";
        public string Password { get; set; } = "devpass";
        public string Table { get; set; } = "serilog_events";
        public string Schema { get; set; } = "public";
        
        public string ConnectionString => 
            $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
    }
}