using Microsoft.Extensions.DependencyInjection;
using Neni.Application;
using Neni.Imaging;
using Neni.Ocr;
using Neni.Platform;
using Neni.Presentation;
using Neni.Translation;

namespace Neni.Tests.Architecture;

// Valida el cableado sin abrir la UI: ValidateOnBuild revisa que cada servicio registrado pueda
// construirse con lo registrado, sin instanciar nada (no abre el portal ni carga modelos).
// Fuera de Linux falla a proposito: AddNeniPlatform todavia no tiene otra implementacion.
public class CompositionTest
{
    /// <summary>Misma cadena de modulos que Neni.Desktop/Program.BuildServices: mantenerlas iguales.</summary>
    [Fact]
    public void Contenedor_resuelve_todos_los_registros_de_las_capas()
    {
        using var provider = new ServiceCollection()
            .AddNeniApplication()
            .AddNeniOcr()
            .AddNeniImaging()
            .AddNeniTranslation()
            .AddNeniPlatform()
            .AddNeniPresentation()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
