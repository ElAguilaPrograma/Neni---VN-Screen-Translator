using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Implementado por capa de plataforma: enumera las ventanas disponibles para que el usuario
// seleccione la ventana objetivo (paso 1 del pipeline).
public interface IWindowLocator
{
    IEnumerable<WindowInfo> ListAvailableWindows();
}
