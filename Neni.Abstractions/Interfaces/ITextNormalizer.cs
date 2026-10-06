using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface ITextNormalizer
{
    // Limpia los artefactos que deja el reconocimiento optico en el texto (espacios, puntuacion,
    // guiones de corte de linea) segun el idioma de origen, sin descartar contenido real.
    string Normalize(string text, Languages sourceLanguage);
}
