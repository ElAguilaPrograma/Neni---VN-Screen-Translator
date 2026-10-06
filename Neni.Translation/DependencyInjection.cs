using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Translation.Services;

namespace Neni.Translation;

public static class TranslationServiceCollectionExtensions
{
    /// <summary>Registra el traductor detras de ITranslatorEngineFactory.</summary>
    public static IServiceCollection AddNeniTranslation(this IServiceCollection services)
    {
        services.AddSingleton<ITranslatorEngineFactory, PassthroughTranslatorEngineFactory>();
        return services;
    }
}
