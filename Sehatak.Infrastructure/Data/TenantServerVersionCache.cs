using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Sehatak.Infrastructure.Data
{
    /// <summary>
    /// كل قواعد بيانات المراكز عايشة على نفس سيرفر MySQL (نفس Host:Port)،
    /// فنسخة السيرفر ثابتة بغض النظر عن اسم قاعدة البيانات. AutoDetect بتفتح
    /// اتصال حقيقي بالسيرفر، فخزناها هون مرة واحدة، ومشترك بين الكلاسين
    /// اللي بيبنوا TenantDbContext.
    /// </summary>
    internal static class TenantServerVersionCache
    {
        private static readonly ConcurrentDictionary<string, ServerVersion> _versions = new();

        public static ServerVersion Get(string connectionString) =>
            _versions.GetOrAdd(connectionString, ServerVersion.AutoDetect);
    }
}