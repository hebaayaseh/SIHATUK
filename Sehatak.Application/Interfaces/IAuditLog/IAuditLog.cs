using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.AuditLog
{
    public interface IAuditLog
    {
        Task LogAsync(string action, string entityType, int? entityId, object? oldValue = null, object? newValue = null);
    }
}
