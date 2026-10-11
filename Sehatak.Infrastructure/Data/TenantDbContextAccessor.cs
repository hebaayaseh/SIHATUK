using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sehatak.Application.Interfaces.IPushNotification;


namespace Sehatak.Infrastructure.Data
{
    public class TenantDbContextAccessor
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IPushNotification _pushSender;

        public TenantDbContextAccessor(
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor,
            IPushNotification pushSender)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _pushSender = pushSender;
        }

        public TenantDbContext GetCurrentTenantDb()
        {
            var centerId = GetCenterIdFromToken();
            var connectionString = BuildConnectionString(centerId);

            var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
            optionsBuilder.UseSqlServer(connectionString);
            optionsBuilder.AddInterceptors(new NotificationPushInterceptor(_pushSender));

            return new TenantDbContext(optionsBuilder.Options);

        }

        private int GetCenterIdFromToken()
        {
            var user = _httpContextAccessor.HttpContext?.User;

            if (user == null)
                throw new UnauthorizedAccessException("User not authenticated");

            var centerIdClaim = user.FindFirst("CenterId")?.Value;

            if (string.IsNullOrEmpty(centerIdClaim))
                throw new UnauthorizedAccessException("CenterId not found in token");

            return int.Parse(centerIdClaim);
        }

        private string BuildConnectionString(int centerId)
        {
            var dbName = $"sehatak_center_{centerId}";
            var template = _config.GetConnectionString("TenantDbTemplate");

            if (string.IsNullOrEmpty(template))
                throw new Exception("TenantDbTemplate is missing in appsettings");

            return $"{template}Database={dbName};";
        }
    }
}
