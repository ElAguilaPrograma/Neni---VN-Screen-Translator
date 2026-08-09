using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Application.Interfaces;

namespace Neni.Application.Services;

// TODO Implementar la interfaz
public class Coordinator 
{
    private readonly IOcr _engine;
    private bool _isActive = false;

    public Coordinator(IOcr engine)
    {
        _engine = engine;
    }

    // Inicia la ejecución la pipeline
    public void StartCycle(int pollIntervalMs = 650)
    {
        if  (pollIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(pollIntervalMs));

        this._isActive = true;
    }

    // Detiene la ejecución de la pipeline
    public void StopCycle()
    {
        if (!this._isActive)
            return;
        
        this._isActive = false;
    }

    public void ProcessCycle()
    {
        if (!this._isActive)
        {
            this.StopCycle();
            return;
        }
        
        this.CaptureAndDispatch();
    }

    // TODO crar el proyecto de test para probar primero capa por capa, ahi implementar un metodo para pasar de PNG a Frame
    private void CaptureAndDispatch(bool forceRun = false)
    {
        
    }
}