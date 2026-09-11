namespace Neni.Presentation.ViewModels;

// Una ROI ya dibujada en RoiSelectionWindow pero todavía sin confirmar. No es la entidad de
// Neni.Abstractions: esa solo se materializa al confirmar (ver RoiSelectionViewModel.Confirm).
public record RoiDraftItem(int RoiId, double X, double Y, double W, double H);
