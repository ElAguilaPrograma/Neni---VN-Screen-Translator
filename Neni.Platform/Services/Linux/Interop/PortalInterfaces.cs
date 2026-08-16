using Tmds.DBus;

namespace Neni.Platform.Services.Linux.Interop;

// Contratos D-Bus de xdg-desktop-portal (org.freedesktop.portal.*).
//
// Tienen que ser publicas: Tmds.DBus genera los proxies en tiempo de ejecucion dentro de otro
// ensamblado dinamico (Tmds.DBus.Emit), y ese ensamblado no puede implementar una interfaz
// internal (TypeLoadException al llamar a CreateProxy).
//
// OJO con "session_handle": como argumento de ENTRADA va tipado como object path (firma "o"),
// pero el portal lo DEVUELVE dentro de los results del Response como string (firma "s").
// Esa asimetria esta en la propia especificacion; ver PortalScreenCastSession.RequestTargetAsync.
[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectSourcesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);
    // Devuelve el descriptor de fichero del remote de PipeWire. El node id por si solo no basta:
    // hay que conectarse a PipeWire a traves de este fd para poder leer el stream.
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    // Propiedades del portal: version, AvailableSourceTypes, AvailableCursorModes.
    Task<T> GetAsync<T>(string prop);
}

// Toda peticion al portal es asincrona: el metodo devuelve el path de un objeto Request y la
// respuesta real llega despues por la senal Response de ese objeto.
[DBusInterface("org.freedesktop.portal.Request")]
public interface IRequest : IDBusObject
{
    Task CloseAsync();
    Task<IDisposable> WatchResponseAsync(
        Action<(uint response, IDictionary<string, object> results)> handler,
        Action<Exception> onError);
}

// La sesion vive mientras viva la conexion D-Bus que la creo. Cerrarla libera el nodo de PipeWire.
[DBusInterface("org.freedesktop.portal.Session")]
public interface ISession : IDBusObject
{
    Task CloseAsync();
    Task<IDisposable> WatchClosedAsync(
        Action<IDictionary<string, object>> handler,
        Action<Exception> onError);
}
