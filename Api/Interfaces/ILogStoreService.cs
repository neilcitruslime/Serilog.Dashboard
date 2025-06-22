
using Serilog.Dashboard.Api.Models;

namespace Api.Interfaces;

public interface ILogStoreService
{
    Task InsertLogsAsync(IEnumerable<SerilogEvent> events);
}