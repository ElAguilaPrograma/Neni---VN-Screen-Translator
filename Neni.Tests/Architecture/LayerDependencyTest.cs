using System.Reflection;
using Neni.Abstractions.Entities;
using Neni.Application;
using Neni.Imaging;
using Neni.Ocr;
using Neni.Platform;
using Neni.Presentation;
using Neni.Translation;

namespace Neni.Tests.Architecture;

// Hace cumplir el diagrama de capas: cada ensamblado Neni.* solo puede depender de las capas que
// tiene permitidas. El compilador solo emite referencias a ensamblados que de verdad se usan, asi
// que esto detecta usos reales, no solo ProjectReference sobrantes.
public class LayerDependencyTest
{
    private static readonly Dictionary<string, (Assembly Assembly, string[] Allowed)> Layers = new()
    {
        ["Neni.Abstractions"] = (typeof(Frame).Assembly, []),
        ["Neni.Application"] = (typeof(ApplicationServiceCollectionExtensions).Assembly, ["Neni.Abstractions"]),
        ["Neni.Ocr"] = (typeof(OcrServiceCollectionExtensions).Assembly, ["Neni.Abstractions"]),
        ["Neni.Imaging"] = (typeof(ImagingServiceCollectionExtensions).Assembly, ["Neni.Abstractions"]),
        ["Neni.Translation"] = (typeof(TranslationServiceCollectionExtensions).Assembly, ["Neni.Abstractions"]),
        ["Neni.Platform"] = (typeof(PlatformServiceCollectionExtensions).Assembly, ["Neni.Abstractions"]),
        ["Neni.Presentation"] = (typeof(PresentationServiceCollectionExtensions).Assembly, ["Neni.Application", "Neni.Abstractions"]),
    };

    public static IEnumerable<object[]> LayerNames() => Layers.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(LayerNames))]
    public void Capa_solo_depende_de_las_capas_permitidas(string layer)
    {
        var (assembly, allowed) = Layers[layer];

        var forbidden = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Neni.", StringComparison.Ordinal) && !allowed.Contains(name))
            .ToList();

        Assert.True(forbidden.Count == 0,
            $"{layer} depende de capas no permitidas: {string.Join(", ", forbidden)}.");
    }
}
