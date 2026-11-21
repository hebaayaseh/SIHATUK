using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Infrastructure.Data;

namespace Sehatak.Infrastructure.Security;

internal static class OtpGuard
{
    public const int MaxAttempts = 5;
    public const int MaxRequestsPer10Min = 3;

    /// يجيب آخر كود صالح. غلط: يزيد المحاولات، وبعد 5 يموت الكود. صح: يعلّمه مستخدم (والمنادي يعمل SaveChanges).
    public static async Task<EmailVerificationCode?> ConsumeAsync(
        TenantDbContext db, int userId, string purpose, string submitted)
    {
        var now = DateTime.UtcNow;
        var code = await db.EmailVerificationCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && !c.IsUsed && c.ExpiresAt > now)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

        if (code == null) return null;

        var same = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(code.Code), Encoding.UTF8.GetBytes(submitted ?? string.Empty));

        if (!same)
        {
            code.Attempts++;
            if (code.Attempts >= MaxAttempts)
                code.ExpiresAt = now;       // انتهى. ما نستعمل IsUsed لأنه يعني "اتفعّل فعلاً"
            await db.SaveChangesAsync();
            return null;
        }

        code.IsUsed = true;
        return code;
    }

    public static Task InvalidateOldAsync(TenantDbContext db, int userId, string purpose)
    {
        var now = DateTime.UtcNow;
        return db.EmailVerificationCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && !c.IsUsed && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now));
    }

    public static Task<bool> HasLiveCodeAsync(TenantDbContext db, int userId, string purpose) =>
        db.EmailVerificationCodes.AnyAsync(c =>
            c.UserId == userId && c.Purpose == purpose && !c.IsUsed && c.ExpiresAt > DateTime.UtcNow);

    public static async Task<bool> TooManyRequestsAsync(TenantDbContext db, int userId, string purpose)
    {
        var since = DateTime.UtcNow.AddMinutes(-10);
        return await db.EmailVerificationCodes.CountAsync(c =>
            c.UserId == userId && c.Purpose == purpose && c.CreatedAt > since) >= MaxRequestsPer10Min;
    }
}