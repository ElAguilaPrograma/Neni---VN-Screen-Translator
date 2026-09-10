using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Neni.Presentation.ViewModels;

namespace Neni.Presentation.Views;

// Los gestos de puntero (arrastrar para dibujar una ROI) y el árbol visual del overlay viven
// acá; toda la matemática de coordenadas (Canvas <-> píxel de frame) vive en el ViewModel para
// que este archivo solo conozca controles de Avalonia, no el modelo de datos.
public partial class RoiSelectionWindow : Window
{
    private Point? _dragStart;
    private Rectangle? _dragVisual;

    public RoiSelectionWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is RoiSelectionViewModel viewModel)
                viewModel.Items.CollectionChanged += (_, _) => RedrawConfirmedRois();
        };

        // NO usar Canvas.LayoutUpdated acá: RedrawConfirmedRois muta RoiOverlayCanvas.Children,
        // y eso por sí mismo dispara un nuevo pase de layout -> nuevo LayoutUpdated -> nueva
        // mutación -> bucle infinito ("Infinite layout loop detected", reproducido apenas había
        // al menos una ROI que dibujar). Resized/Opened solo se disparan por cambios reales de
        // tamaño de ventana, no como efecto secundario de nuestro propio redibujado.
        Opened += (_, _) => RedrawConfirmedRois();
        Resized += (_, _) => RedrawConfirmedRois();

        Closed += (_, _) => (DataContext as RoiSelectionViewModel)?.Dispose();
    }

    private void RoiOverlayCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RoiSelectionViewModel viewModel || !viewModel.CanAddRoi)
            return;

        viewModel.BeginDrag();
        _dragStart = e.GetPosition(RoiOverlayCanvas);
        _dragVisual = new Rectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        RoiOverlayCanvas.Children.Add(_dragVisual);
        e.Pointer.Capture(RoiOverlayCanvas);
    }

    private void RoiOverlayCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || _dragVisual is null)
            return;

        var current = e.GetPosition(RoiOverlayCanvas);
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);

        Canvas.SetLeft(_dragVisual, x);
        Canvas.SetTop(_dragVisual, y);
        _dragVisual.Width = Math.Abs(current.X - start.X);
        _dragVisual.Height = Math.Abs(current.Y - start.Y);
    }

    private void RoiOverlayCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is not { } start || DataContext is not RoiSelectionViewModel viewModel)
            return;

        var end = e.GetPosition(RoiOverlayCanvas);

        if (_dragVisual is not null)
            RoiOverlayCanvas.Children.Remove(_dragVisual);

        _dragVisual = null;
        _dragStart = null;
        e.Pointer.Capture(null);

        viewModel.EndDrag();
        viewModel.CommitDrag(start, end, RoiOverlayCanvas.Bounds.Size);
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as RoiSelectionViewModel)?.Confirm();
        Close();
    }

    // Redibuja los rectángulos de las ROIs ya confirmadas (no el rectángulo de arrastre en
    // curso, que se maneja aparte en los handlers de puntero de arriba).
    private void RedrawConfirmedRois()
    {
        if (DataContext is not RoiSelectionViewModel viewModel || _dragStart is not null)
            return;

        RoiOverlayCanvas.Children.Clear();
        var size = RoiOverlayCanvas.Bounds.Size;

        foreach (var item in viewModel.Items)
        {
            var rect = viewModel.ToControlRect(item, size);

            var shape = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(shape, rect.X);
            Canvas.SetTop(shape, rect.Y);
            RoiOverlayCanvas.Children.Add(shape);

            var label = new TextBlock
            {
                Text = $"#{item.RoiId}",
                Foreground = Brushes.Cyan,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, rect.X + 2);
            Canvas.SetTop(label, rect.Y + 2);
            RoiOverlayCanvas.Children.Add(label);
        }
    }
}
