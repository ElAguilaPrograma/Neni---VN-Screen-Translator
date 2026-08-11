using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Implementado por capa de plataforma: enumera las ventanas disponibles para que el usuario
// seleccione la ventana objetivo (paso 1 del pipeline).
public interface IWindowLocator
{
    // Lista todas las ventanas disponibles en el sistema, devolviendo una colección de objetos WindowInfo que contienen información sobre cada ventana.
    IEnumerable<WindowInfo> ListAvailableWindows();
    // Obtiene información detallada sobre una ventana específica identificada por su handle, devolviendo un objeto WindowInfo.
    WindowInfo GetWindowInfo(IntPtr handle);
}
