using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface IOverlay : IAsyncDisposable
{
    // Inicializa la superposición de traducción para la ventana objetivo especificada, preparando el entorno para mostrar los elementos de traducción.
    Task InitializeAsync();
    // Detiene la superposición de traducción, liberando los recursos asociados y cerrando cualquier ventana o elemento visual relacionado con la superposición.
    Task StopAsync();
    // Renderiza los elementos de traducción proporcionados en la superposición, mostrando el texto traducido en las posiciones y estilos especificados.
    Task RenderTranslationOverlayAsync(IEnumerable<TranslationOverlayItem> items);
    // Actualiza el contenido de la superposición con un solo elemento de traducción, permitiendo cambios dinámicos en la visualización sin necesidad de volver a renderizar toda la superposición.
    void UpdateOverlayContent(TranslationOverlayItem item);
    // Elimina un elemento de traducción específico de la superposición, identificado por su ID, eliminando su visualización de la pantalla.
    void RemoveOverlayContent(int itemId);
    // Obtiene la capacidad actual de la superposición.
    OverlayCapability CurrentOverlayCapability { get; }
}