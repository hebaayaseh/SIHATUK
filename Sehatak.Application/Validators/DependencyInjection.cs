using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Sehatak.Application.Validators
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers every AbstractValidator in this assembly.
        /// Call it from Program.cs before builder.Build().
        /// </summary>
        public static IServiceCollection AddSehatakValidators(this IServiceCollection services)
        {
            services.AddValidatorsFromAssemblyContaining(typeof(DependencyInjection), ServiceLifetime.Singleton);

            // Report every broken rule in one response instead of stopping at the
            // first failure per property — the client can then fix the whole form
            // in a single round trip.
            ValidatorOptions.Global.DefaultClassLevelCascadeMode = CascadeMode.Continue;
            ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

            // Property names in the payload are camelCase/inconsistent across DTOs,
            // so emit the raw member name rather than FluentValidation's
            // "Pretty Printed" split (which would turn "dateOnly" into "date Only").
            ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => member?.Name;

            return services;
        }
    }
}
