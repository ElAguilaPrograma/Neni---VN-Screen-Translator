using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

// Implemendado por la capa Plataforma, permite a la capa de logica de negocio conocer el entorno de plataforma en el que se esta ejecutando.
public interface IPlatformEnvironment
{
    // Obtiene el tipo de servidor de visualización actual (X11, Wayland, Windows Desktop, etc.) en el que se está ejecutando la aplicación.
    DisplayServerType GetDisplayServerType();
    // Obtiene la capacidad de superposición actual del sistema, indicando si se puede usar una superposición directa, una superposición LayerShell o solo una ventana de acompañante.
    OverlayCapability GetOverlayCapability();
    // Determina si la superposición actual soporta la funcionalidad de "click-through", permitiendo que los clics del ratón pasen a través de la superposición hacia las ventanas subyacentes.
    bool SupportsClickThrough();
}