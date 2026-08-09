using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Tests.Ocr.Helpers;
using Xunit.Abstractions;

namespace Neni.Tests;

[Collection(OcrEngineCollection.Name)]
public class OcrTest
{
    private const string EnglishFramesDirectory = "Ocr/Frames/English/All";

    private readonly IOcr _engine;
    private readonly ITestOutputHelper _output;

    public OcrTest(OcrEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _output = output;
    }

    // Enumera los PNG del directorio de frames en ingles al momento de descubrir los tests.
    public static IEnumerable<object[]> EnglishFrames() =>
        Directory.EnumerateFiles(EnglishFramesDirectory, "*.png")
            .Order()
            .Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(EnglishFrames))]
    public async Task DetectAsync_extrae_texto_de_frame_en_ingles(string fileName)
    {
        Frame frame = TestFrameLoader.FromPngFile(Path.Combine(EnglishFramesDirectory, fileName));

        OcrResult result = await _engine.DetectAsync(frame);

        _output.WriteLine($"[{fileName}]");
        _output.WriteLine(result.FullText);

        Assert.NotEmpty(result.Blocks);
    }
}
