using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Application.Pipeline;

namespace Neni.Tests.Application;

public class OverlayTrackerTest
{
    private static readonly RegionOfInterest Roi = new(1, 100, 200, 400, 100);

    [Fact]
    public async Task Las_cajas_del_OCR_se_pasan_a_coordenadas_de_ventana()
    {
        var overlay = new RecordingOverlay();
        var tracker = new OverlayTracker(overlay);

        await tracker.ApplyAsync(Roi, [Block(0, 10, 10, 50, 30)]);

        var item = Assert.Single(overlay.Rendered);
        Assert.Equal(new WindowBounds(110, 210, 40, 20), item.Bounds);
    }

    [Fact]
    public async Task Actualiza_los_bloques_que_siguen_y_quita_los_que_desaparecen()
    {
        var overlay = new RecordingOverlay();
        var tracker = new OverlayTracker(overlay);
        await tracker.ApplyAsync(Roi, [Block(0, 0, 0, 10, 10), Block(1, 0, 20, 10, 30)]);

        await tracker.ApplyAsync(Roi, [Block(0, 0, 0, 10, 10)]);

        Assert.Equal([1000], overlay.Updated.Select(i => i.OverlayItemId));
        Assert.Equal([1001], overlay.Removed);
    }

    [Fact]
    public async Task Forget_quita_de_pantalla_todo_lo_de_la_ROI()
    {
        var overlay = new RecordingOverlay();
        var tracker = new OverlayTracker(overlay);
        await tracker.ApplyAsync(Roi, [Block(0, 0, 0, 10, 10), Block(1, 0, 20, 10, 30)]);

        tracker.Forget(Roi.RoiId);

        Assert.Equal([1000, 1001], overlay.Removed.Order());
    }

    [Fact]
    public async Task En_modo_ventana_de_acompanante_no_toca_el_overlay()
    {
        var overlay = new RecordingOverlay { Capability = OverlayCapability.CompanionWindowOnly };
        var tracker = new OverlayTracker(overlay);

        await tracker.ApplyAsync(Roi, [Block(0, 0, 0, 10, 10)]);

        Assert.Empty(overlay.Rendered);
    }

    private static TranslatedBlock Block(int index, float left, float top, float right, float bottom)
        => new(index, $"src{index}", $"dst{index}",
            [new(left, top), new(right, top), new(right, bottom), new(left, bottom)]);

    private sealed class RecordingOverlay : IOverlay
    {
        public OverlayCapability Capability { get; init; } = OverlayCapability.LayerShellOverlay;
        public List<TranslationOverlayItem> Rendered { get; } = [];
        public List<TranslationOverlayItem> Updated { get; } = [];
        public List<int> Removed { get; } = [];

        public OverlayCapability CurrentOverlayCapability => Capability;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task RenderTranslationOverlayAsync(IEnumerable<TranslationOverlayItem> items)
        {
            Rendered.AddRange(items);
            return Task.CompletedTask;
        }

        public void UpdateOverlayContent(TranslationOverlayItem item) => Updated.Add(item);
        public void RemoveOverlayContent(int itemId) => Removed.Add(itemId);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
