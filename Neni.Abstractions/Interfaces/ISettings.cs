using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface ISettings
{
    // Lee el archivo de configuracion JSON y lo carga en la clase Settings, si no existe el archivo, se crea uno con los valores por defecto.
    Settings Load();
    // Guarda la clase Settings en el archivo de configuracion JSON
    void Save(Settings settings);
}