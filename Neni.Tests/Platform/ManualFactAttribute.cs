namespace Neni.Tests.Platform;

// Fact que solo corre si la variable de entorno NENI_MANUAL_TESTS=1 esta definida. Para tests
// que necesitan a una persona frente a la pantalla (dialogo del portal), que en un dotnet test
// normal se quedarian colgados esperando una seleccion que nunca llega.
public sealed class ManualFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "NENI_MANUAL_TESTS";

    public ManualFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
            Skip = $"Test manual: requiere interaccion con el portal. Define {EnvironmentVariable}=1 para correrlo.";
    }
}
