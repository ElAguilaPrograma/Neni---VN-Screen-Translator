namespace Neni.Application.DataTransferObjets;

public record RegionOfInterestDto(
    int RoiId,
    double X,
    double Y,
    double W,
    double H,
    double Scale);