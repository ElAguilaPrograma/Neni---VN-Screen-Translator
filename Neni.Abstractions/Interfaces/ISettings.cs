using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface ISettings
{
    Settings Load();
    void Save(Settings settings);
}