using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Ocr.Services;

// Adaptador de ITextNormalizer sobre TextNormalizer: elige el perfil por sistema de escritura a
// partir del idioma de origen.
internal sealed class ScriptTextNormalizer : ITextNormalizer
{
    public string Normalize(string text, Languages sourceLanguage)
        => TextNormalizer.Normalize(text, TextNormalizer.ScriptFor(sourceLanguage));
}
