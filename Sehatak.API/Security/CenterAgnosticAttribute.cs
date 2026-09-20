namespace Sehatak.API.Security
{
    /// <summary>
    /// حط هاد الـ attribute بس على endpoint بترجع بيانات عامة مشتركة بين كل
    /// المراكز (زي كتالوج الخطط أو المزايا، المخزّنين بـ SharedDbContext).
    /// أي endpoint تاني بدون centerId بالـ route، بدون هاد الـ attribute،
    /// رح يترفض تلقائياً من CenterAccessFilter — هيك أي دفعر جديد بينسى يحط
    /// centerId بالـ route بياخد خطأ واضح بدل ما يمرّ بصمت.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CenterAgnosticAttribute : Attribute
    {
    }
}