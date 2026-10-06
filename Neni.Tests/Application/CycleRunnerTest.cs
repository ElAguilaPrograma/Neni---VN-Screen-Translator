using Neni.Abstractions.Entities;
using Neni.Application.Interfaces;

namespace Neni.Tests.Application;

public class CycleRunnerTest
{
    private static readonly IReadOnlyList<RegionOfInterest> Rois = [new(1, 0, 0, 10, 1)];

    [Fact]
    public async Task Mismo_frame_y_mismas_ROIs_no_vuelve_a_procesar_nada()
    {
        var (runner, engines, _) = CoordinatorFakes.CreateCycleRunner();

        var first = await runner.ProcessTurnAsync(Rois, CancellationToken.None);
        var second = await runner.ProcessTurnAsync(Rois, CancellationToken.None);

        // La deduplicacion falsa nunca descarta nada: si el OCR corrio una sola vez, fue el corte
        // por frame completo el que salto la segunda vuelta.
        Assert.Equal(1, engines.FakeOcr.Detections);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Un_fallo_conserva_el_texto_anterior_y_se_reporta_hasta_que_la_ROI_se_recupera()
    {
        var (runner, engines, frameCapture) = CoordinatorFakes.CreateCycleRunner();
        await runner.ProcessTurnAsync(Rois, CancellationToken.None);

        frameCapture.NextFrame();
        engines.FakeOcr.FailNextDetect = true;
        var failed = await runner.ProcessTurnAsync(Rois, CancellationToken.None);

        frameCapture.NextFrame();
        var recovered = await runner.ProcessTurnAsync(Rois, CancellationToken.None);

        Assert.Equal("10", failed[1].Text);
        Assert.Equal("Fallo simulado del OCR.", failed[1].Error);
        Assert.Equal(new RoiReport("10"), recovered[1]);
    }

    [Fact]
    public async Task Se_puede_volver_a_arrancar_despues_de_StopAsync()
    {
        var (runner, _, _) = CoordinatorFakes.CreateCycleRunner();

        for (var i = 0; i < 2; i++)
        {
            var firstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = Task.Run(() => runner.RunAsync(() => Rois, new SyncProgress(_ => firstReport.TrySetResult())));

            await firstReport.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await runner.StopAsync();
            await run;
        }
    }

    [Fact]
    public async Task Si_el_ciclo_muere_solo_se_puede_volver_a_arrancar()
    {
        var (runner, _, frameCapture) = CoordinatorFakes.CreateCycleRunner();
        frameCapture.FailNextGrab = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(() => Rois, null));

        var firstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() => runner.RunAsync(() => Rois, new SyncProgress(_ => firstReport.TrySetResult())));
        await firstReport.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.StopAsync();
        await run;
    }

    // IProgress sin SynchronizationContext: reporta en el mismo hilo del ciclo.
    private sealed class SyncProgress(Action<IReadOnlyDictionary<int, RoiReport>> onReport)
        : IProgress<IReadOnlyDictionary<int, RoiReport>>
    {
        public void Report(IReadOnlyDictionary<int, RoiReport> value) => onReport(value);
    }
}
