using System.Collections;
using System.Runtime.CompilerServices;
using Neni.Abstractions.Entities;
using Neni.Platform.Services.Linux.Interop;
using Tmds.DBus;

namespace Neni.Platform.Services.Linux;

// Sesion de ScreenCast de xdg-desktop-portal.
//
// La sesion PERTENECE a la conexion D-Bus que la creo: si esa conexion se cierra, el portal
// cierra la sesion y destruye el nodo de PipeWire. Por eso esta clase es de vida larga
// (registrarla como singleton) y la comparten el selector de ventana y el IFrameCapture:
// el node id que devuelve solo es valido mientras esta instancia siga viva.
public sealed class PortalScreenCastSession : IAsyncDisposable
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalObject = "/org/freedesktop/portal/desktop";

    // Mascaras de bits definidas por el portal para SourceTypes y CursorModes.
    private const uint SourceTypeMonitor = 1;
    private const uint SourceTypeWindow = 2;
    private const uint CursorModeHidden = 1;
    // persist_mode 2 = el permiso persiste hasta que el usuario lo revoque.
    private const uint PersistModeUntilRevoked = 2;

    // Codigos de la senal Response: 0 exito, 1 el usuario cancelo, 2 cualquier otro final.
    private const uint ResponseSuccess = 0;
    private const uint ResponseUserCancelled = 1;

    private readonly PortalRestoreTokenStore _restoreTokens = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Connection? _connection;
    private IScreenCast? _screenCast;
    private ISession? _session;
    private ObjectPath _sessionHandle;
    private string _localName = string.Empty;
    private int _requestCounter;
    private bool _disposed;

    // Nodo de PipeWire de la fuente seleccionada, valido mientras la sesion siga abierta.
    public uint? NodeId { get; private set; }

    // Descriptor del remote de PipeWire. IFrameCapture lo necesita para abrir el stream.
    public CloseSafeHandle? PipeWireRemote { get; private set; }

    public bool IsOpen => NodeId.HasValue;

    // Abre una sesion de captura y devuelve la fuente elegida, o null si el usuario cancelo.
    //
    // reuseLastGrant: si hay un restore_token guardado de una sesion anterior, lo enviamos para
    // que el portal reabra la misma fuente sin mostrar el dialogo. Debe ser false cuando el
    // usuario pide explicitamente elegir ventana, o siempre reabriria la ventana antigua.
    public async Task<CaptureTarget?> RequestTargetAsync(
        bool reuseLastGrant = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Una sesion por captura: si habia otra abierta, la cerramos antes de pedir la nueva.
            await CloseSessionAsync();

            try
            {
                return await OpenSessionAsync(reuseLastGrant, cancellationToken);
            }
            catch
            {
                // Si algo falla a mitad del handshake no dejamos la sesion a medias en el portal.
                await CloseSessionAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CaptureTarget?> OpenSessionAsync(bool reuseLastGrant, CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectedAsync();
        var screenCast = _screenCast!;

        var availableSourceTypes = await screenCast.GetAsync<uint>("AvailableSourceTypes");
        var requestedSourceTypes = availableSourceTypes & (SourceTypeWindow | SourceTypeMonitor);

        if (requestedSourceTypes == 0)
            throw new NotSupportedException(
                "El portal de este escritorio no ofrece captura de ventanas ni de monitores.");

        // 1. CreateSession
        var createResponse = await CallPortalAsync(
            connection,
            handleToken => screenCast.CreateSessionAsync(new Dictionary<string, object>
            {
                ["handle_token"] = handleToken,
                ["session_handle_token"] = $"neni_{Guid.NewGuid():N}"
            }),
            cancellationToken);

        if (!ShouldContinue(createResponse))
            return null;

        // El portal devuelve session_handle como STRING, no como ObjectPath (ver PortalInterfaces).
        if (!createResponse.Results.TryGetValue("session_handle", out var rawSessionHandle)
            || rawSessionHandle is not string sessionHandleText)
            throw new InvalidOperationException(
                "El portal no devolvio un session_handle valido al crear la sesion de ScreenCast.");

        _sessionHandle = new ObjectPath(sessionHandleText);
        _session = connection.CreateProxy<ISession>(PortalService, _sessionHandle);

        // 2. SelectSources: queremos ventanas (o monitores si el portal no soporta ventanas),
        // sin cursor, y con permiso persistente para no repetir el dialogo en cada arranque.
        var selectOptions = new Dictionary<string, object>
        {
            ["types"] = requestedSourceTypes,
            ["multiple"] = false,
            ["cursor_mode"] = CursorModeHidden,
            ["persist_mode"] = PersistModeUntilRevoked
        };

        if (reuseLastGrant && _restoreTokens.Load() is { } restoreToken)
            selectOptions["restore_token"] = restoreToken;

        var selectResponse = await CallPortalAsync(
            connection,
            handleToken =>
            {
                selectOptions["handle_token"] = handleToken;
                return screenCast.SelectSourcesAsync(_sessionHandle, selectOptions);
            },
            cancellationToken);

        if (!ShouldContinue(selectResponse))
            return null;

        // 3. Start: aqui es donde el compositor abre su dialogo nativo de seleccion
        // (o lo omite si el restore_token seguia siendo valido).
        var startResponse = await CallPortalAsync(
            connection,
            handleToken => screenCast.StartAsync(_sessionHandle, string.Empty, new Dictionary<string, object>
            {
                ["handle_token"] = handleToken
            }),
            cancellationToken);

        if (!ShouldContinue(startResponse))
            return null;

        // El portal rota el token en cada Start, asi que guardamos siempre el ultimo.
        _restoreTokens.Save(startResponse.Results.TryGetValue("restore_token", out var rawRestoreToken)
            ? rawRestoreToken as string
            : null);

        if (!TryReadFirstStreamNodeId(startResponse.Results, out var nodeId))
            throw new InvalidOperationException(
                "El portal acepto la captura pero no devolvio ningun stream de PipeWire.");

        PipeWireRemote = await screenCast.OpenPipeWireRemoteAsync(
            _sessionHandle,
            new Dictionary<string, object>());

        NodeId = nodeId;

        return new CaptureTarget(
            Id: $"pw:{nodeId}",
            Name: $"Ventana seleccionada (PipeWire {nodeId})",
            NativeHandle: IntPtr.Zero,
            PipeWireNodeId: nodeId);
    }

    // true: seguimos. false: el usuario cancelo el dialogo, que no es un error.
    // Cualquier otro codigo si lo es y lo propagamos para que la capa de UI lo muestre.
    private static bool ShouldContinue(PortalResponse response)
    {
        if (response.IsSuccess)
            return true;

        if (response.WasCancelledByUser)
            return false;

        throw new InvalidOperationException(
            $"El portal termino la peticion de forma inesperada (response={response.Code}).");
    }

    // Invoca un metodo del portal y espera su senal Response.
    //
    // El portal puede emitir Response ANTES de que la llamada D-Bus devuelva el path del objeto
    // Request, asi que calculamos nosotros ese path a partir del handle_token y nos suscribimos
    // antes de invocar. La especificacion del portal recomienda explicitamente hacerlo asi para
    // evitar esa carrera; suscribirse con el path devuelto puede perder la respuesta.
    private async Task<PortalResponse> CallPortalAsync(
        Connection connection,
        Func<string, Task<ObjectPath>> invokeAsync,
        CancellationToken cancellationToken)
    {
        var handleToken = $"neni_{Interlocked.Increment(ref _requestCounter)}";
        var expectedRequestPath = BuildRequestPath(handleToken);

        var responseSource = new TaskCompletionSource<PortalResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var watchers = new List<IDisposable>(2);

        try
        {
            watchers.Add(await WatchResponseAsync(connection, expectedRequestPath, responseSource));

            var actualRequestPath = await invokeAsync(handleToken).WaitAsync(cancellationToken);

            // Red de seguridad por si algun portal no respeta nuestro handle_token.
            if (!actualRequestPath.Equals(expectedRequestPath))
                watchers.Add(await WatchResponseAsync(connection, actualRequestPath, responseSource));

            await using var cancellationRegistration = cancellationToken.Register(
                () => responseSource.TrySetCanceled(cancellationToken));

            return await responseSource.Task;
        }
        finally
        {
            foreach (var watcher in watchers)
                watcher.Dispose();
        }
    }

    private static Task<IDisposable> WatchResponseAsync(
        Connection connection,
        ObjectPath requestPath,
        TaskCompletionSource<PortalResponse> responseSource)
    {
        var request = connection.CreateProxy<IRequest>(PortalService, requestPath);

        // El callback de error es imprescindible: sin el, un fallo a nivel D-Bus se pierde
        // y el await de la respuesta se queda colgado para siempre.
        return request.WatchResponseAsync(
            arg => responseSource.TrySetResult(
                new PortalResponse(arg.response, arg.results ?? new Dictionary<string, object>())),
            exception => responseSource.TrySetException(exception));
    }

    // La spec define la ruta del objeto Request como
    // /org/freedesktop/portal/desktop/request/<SENDER>/<handle_token>, donde SENDER es el nombre
    // unico de nuestra conexion sin los ':' iniciales y con los '.' sustituidos por '_'.
    private ObjectPath BuildRequestPath(string handleToken)
    {
        var sender = _localName.TrimStart(':').Replace('.', '_');
        return new ObjectPath($"{PortalObject}/request/{sender}/{handleToken}");
    }

    // "streams" tiene firma a(ua{sv}): un array de STRUCTS (node id, propiedades), no un array de
    // arrays. Tmds.DBus materializa los structs de D-Bus como ValueTuple, asi que leemos el primer
    // campo via ITuple en lugar de castear a un tipo concreto: distintas versiones del portal
    // traen distintas propiedades y no queremos atarnos a una forma exacta.
    private static bool TryReadFirstStreamNodeId(IDictionary<string, object> results, out uint nodeId)
    {
        nodeId = 0;

        if (!results.TryGetValue("streams", out var rawStreams) || rawStreams is not IEnumerable streams)
            return false;

        foreach (var stream in streams)
        {
            switch (stream)
            {
                case ITuple tuple when tuple.Length > 0 && tuple[0] is uint tupleNodeId:
                    nodeId = tupleNodeId;
                    return true;
                case object[] { Length: > 0 } fields when fields[0] is uint arrayNodeId:
                    nodeId = arrayNodeId;
                    return true;
            }
        }

        return false;
    }

    private async Task<Connection> EnsureConnectedAsync()
    {
        if (_connection is not null)
            return _connection;

        var address = Address.Session
            ?? throw new InvalidOperationException(
                "No hay bus de sesion D-Bus disponible (DBUS_SESSION_BUS_ADDRESS no esta definido).");

        var connection = new Connection(address);
        var connectionInfo = await connection.ConnectAsync();

        _localName = connectionInfo.LocalName;
        _screenCast = connection.CreateProxy<IScreenCast>(PortalService, PortalObject);
        _connection = connection;

        return connection;
    }

    private async Task CloseSessionAsync()
    {
        PipeWireRemote?.Dispose();
        PipeWireRemote = null;
        NodeId = null;

        if (_session is null)
            return;

        var session = _session;
        _session = null;

        try
        {
            await session.CloseAsync();
        }
        catch (DBusException)
        {
            // El compositor pudo haber cerrado ya la sesion por su cuenta.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _gate.WaitAsync();
        try
        {
            await CloseSessionAsync();
            _connection?.Dispose();
            _connection = null;
            _screenCast = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private readonly record struct PortalResponse(uint Code, IDictionary<string, object> Results)
    {
        public bool IsSuccess => Code == ResponseSuccess;
        public bool WasCancelledByUser => Code == ResponseUserCancelled;
    }
}
