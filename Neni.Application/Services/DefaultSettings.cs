using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Services;

// Placeholder TEMPORAL: devuelve los valores por defecto de Settings sin tocar disco.
// Reemplazar por la implementación real (leer/crear el JSON de configuración, según
// documenta ISettings) cuando se implemente ISettings de verdad en esta capa.
public sealed class DefaultSettings : ISettings
{
    public Settings Load() => new();

    public void Save(Settings settings)
    {
        // No-op temporal: todavía no hay persistencia real.
    }
}
