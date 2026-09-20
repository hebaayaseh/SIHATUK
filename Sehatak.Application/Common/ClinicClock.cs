namespace Sehatak.Application.Common
{
    /// <summary>
    /// "اليوم" و"الآن" حسب توقيت العيادة (فلسطين)، مو UTC. التخزين بالداتابيس
    /// لازم يضل UTC (ما تغيّر هذا)، بس أي مقارنة مع تاريخ تقويمي — هل هذا
    /// التاريخ بالماضي، هل هذا موعد اليوم، هل الطبيب على شفت هلأ — لازم
    /// تستخدم هذا الكلاس بدل DateTime.UtcNow مباشرة. الفرق بين UTC والتوقيت
    /// المحلي (+2/+3) بيقلب "اليوم" لـ "أمس" بين منتصف الليل والساعة 2-3 صباحاً.
    /// </summary>
    public static class ClinicClock
    {
        // "Asia/Hebron" هي الـ IANA ID الرسمية لفلسطين (غزة والضفة). شغالة
        // على .NET 8 بكل المنصات (Linux/Windows) بفضل ICU المدمجة.
        private static readonly TimeZoneInfo Zone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");

        public static DateTime Now =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        public static DateOnly Today =>
            DateOnly.FromDateTime(Now);

    }
}