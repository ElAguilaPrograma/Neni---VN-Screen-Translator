namespace Neni.Platform.Services.Linux;

// Guarda el "restore_token" que devuelve el portal cuando se pide persist_mode. Con ese token
// podemos volver a abrir la misma fuente en arranques posteriores sin que el usuario tenga que
// volver a aprobar el dialogo de captura cada vez que abre Neni.
//
// No va en Settings a proposito: es un dato especifico de Linux/portal, no una preferencia del
// usuario, y no tendria sentido en el archivo de configuracion que comparten todas las plataformas.
//
// OJO: no todos los backends del portal devuelven restore_token. Comprobado el 2026-08-14 contra
// xdg-desktop-portal-hyprland (version 6 del portal): acepta persist_mode pero Start solo devuelve
// "streams" y "source_type", sin token. Ahi el dialogo se mostrara siempre; en GNOME/KDE, que si lo
// implementan, se saltara. Todo el camino esta escrito para tolerar la ausencia de token.
internal sealed class PortalRestoreTokenStore
{
    private readonly string _tokenFilePath;

    public PortalRestoreTokenStore()
    {
        // En Linux ApplicationData resuelve a $XDG_CONFIG_HOME (o ~/.config).
        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "neni");

        _tokenFilePath = Path.Combine(configDirectory, "screencast-restore-token");
    }

    public string? Load()
    {
        try
        {
            if (!File.Exists(_tokenFilePath))
                return null;

            var token = File.ReadAllText(_tokenFilePath).Trim();
            return string.IsNullOrEmpty(token) ? null : token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin token solo perdemos la comodidad de saltarnos el dialogo, no la funcionalidad.
            return null;
        }
    }

    public void Save(string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token))
            {
                File.Delete(_tokenFilePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_tokenFilePath)!);
            File.WriteAllText(_tokenFilePath, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Idem: no poder cachear el token no debe romper la captura.
        }
    }
}
