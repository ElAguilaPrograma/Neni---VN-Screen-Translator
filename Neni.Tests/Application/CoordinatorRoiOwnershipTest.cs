using Neni.Abstractions.Entities;
using Neni.Application.Interfaces;

namespace Neni.Tests.Application;

// El Coordinator es el unico dueno de las ROIs: la UI las reemplaza o borra en cualquier momento,
// incluso con el ciclo corriendo, y el texto de cada ROI siempre corresponde a la lista actual.
public class CoordinatorRoiOwnershipTest
{
    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void SetRegionsOfInterest_y_DeleteRegionOfInterest_mantienen_una_sola_lista()
    {
        var coordinator = CoordinatorFakes.CreateCoordinator();

        coordinator.SetRegionsOfInterest([Roi(1, 10), Roi(2, 20)]);
        coordinator.DeleteRegionOfInterest(1);

        Assert.Equal([Roi(2, 20)], coordinator.RegionsOfInterest);
    }

    [Fact]
    public void SetRegionsOfInterest_rechaza_ids_repetidos()
    {
        var coordinator = CoordinatorFakes.CreateCoordinator();

        Assert.Throws<ArgumentException>(() => coordinator.SetRegionsOfInterest([Roi(1, 10), Roi(1, 20)]));
    }

    [Fact]
    public async Task Cambiar_ROIs_con_el_ciclo_corriendo_se_refleja_en_la_siguiente_vuelta()
    {
        await using var coordinator = CoordinatorFakes.CreateCoordinator();
        var reports = new ReportWaiter();

        coordinator.SetRegionsOfInterest([Roi(1, 10), Roi(2, 20)]);
        var cycle = Task.Run(() => coordinator.StartCycle(reports));

        await reports.WaitForAsync(texts => Matches(texts, (1, "10"), (2, "20")), ReportTimeout);

        // Borrar la ROI 2 y cambiar la geometria de la 1 sin detener el ciclo: el texto viejo de
        // ambas tiene que desaparecer, no quedarse colgado del estado anterior.
        coordinator.SetRegionsOfInterest([Roi(1, 30)]);
        await reports.WaitForAsync(texts => Matches(texts, (1, "30")), ReportTimeout);

        coordinator.DeleteRegionOfInterest(1);
        await reports.WaitForAsync(texts => texts.Count == 0, ReportTimeout);

        await coordinator.StopCycle();
        await cycle;
    }

    private static RegionOfInterest Roi(int id, double width) => new(id, 0, 0, width, 1);

    private static bool Matches(IReadOnlyDictionary<int, RoiReport> texts, params (int RoiId, string Text)[] expected)
        => texts.Count == expected.Length
           && expected.All(e => texts.TryGetValue(e.RoiId, out var report) && report.Text == e.Text);

    // IProgress sincrono (sin SynchronizationContext) que permite esperar a que llegue un reporte
    // que cumpla una condicion.
    private sealed class ReportWaiter : IProgress<IReadOnlyDictionary<int, RoiReport>>
    {
        private readonly Lock _gate = new();
        private Func<IReadOnlyDictionary<int, RoiReport>, bool>? _predicate;
        private TaskCompletionSource? _match;

        public void Report(IReadOnlyDictionary<int, RoiReport> value)
        {
            lock (_gate)
            {
                if (_predicate is not null && _predicate(value))
                    _match?.TrySetResult();
            }
        }

        public async Task WaitForAsync(Func<IReadOnlyDictionary<int, RoiReport>, bool> predicate, TimeSpan timeout)
        {
            var match = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _predicate = predicate;
                _match = match;
            }

            await match.Task.WaitAsync(timeout);
        }
    }
}
