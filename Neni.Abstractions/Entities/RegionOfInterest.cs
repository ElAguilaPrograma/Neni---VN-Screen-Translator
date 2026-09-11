namespace Neni.Abstractions.Entities;

public record RegionOfInterest(
    int RoiId,
    double X,
    double Y,
    double W,
    double H);