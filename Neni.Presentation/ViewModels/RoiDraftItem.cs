using CommunityToolkit.Mvvm.ComponentModel;

namespace Neni.Presentation.ViewModels;

// Estado mutable de una ROI MIENTRAS se está dibujando/editando en RoiSelectionWindow.
// No es la entidad inmutable de Neni.Abstractions: esa solo se materializa al confirmar
// (ver RoiSelectionViewModel.ToRegionOfInterestList). X/Y/W/H quedan fijos una vez dibujada
// la caja; solo Scale es editable después (spinner en la UI), de ahí que sea lo único observable.
public partial class RoiDraftItem : ObservableObject
{
    public int RoiId { get; }
    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }

    // decimal (no double) porque NumericUpDown de Avalonia bindea Value como decimal?.
    [ObservableProperty]
    public partial decimal Scale { get; set; }

    public RoiDraftItem(int roiId, double x, double y, double w, double h, decimal scale)
    {
        RoiId = roiId;
        X = x;
        Y = y;
        W = w;
        H = h;
        Scale = scale;
    }
}
