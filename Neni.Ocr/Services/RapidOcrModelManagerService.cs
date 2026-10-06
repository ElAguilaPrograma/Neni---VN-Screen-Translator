using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Ocr.Services;

internal sealed record RapidOcrModelPaths(
    string DetectionModelPath,
    string ClassificationModelPath,
    string RecognitionModelPath,
    string DictionaryPath);

// Gestiona los modelos de PP-OCRv6 (tiny/small/medium). PP-OCRv6 no publica un clasificador
// de orientacion propio, asi que se reutiliza el clasificador de PP-OCRv5 como dependencia
// compartida entre los 3 tamaños (ver README de RapidOcrNet).
internal sealed class RapidOcrModelManagerService
{
    private const string ModelScopeBaseUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2";

    // Clasificador de orientacion (180°) heredado de PP-OCRv5: PP-OCRv6 no publica uno propio.
    private const string ClsFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";

    private readonly string _modelsRootPath;
    private readonly HttpClient _httpClient;

    public RapidOcrModelManagerService(string modelsRootPath = "Models", HttpClient? httpClient = null)
    {
        _modelsRootPath = modelsRootPath;
        _httpClient = httpClient ?? new HttpClient();

        // ModelScope's CDN (Tengine) devuelve 403 "denied by UA ACL = blacklist" a requests
        // sin User-Agent, que es lo que HttpClient envia por defecto. Sin este header la
        // descarga falla siempre, sin importar que la URL firmada sea valida.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Neni-VN-Screen-Translator/1.0");
    }

    // Verifica que los modelos de PP-OCRv6 del tamaño solicitado existan en disco (descarga lo que falte)
    public async Task<RapidOcrModelPaths> DetectOcrModelAsync(OcrModelSize modelSize,
        CancellationToken cancellationToken = default)
    {
        string folder = Path.Combine(_modelsRootPath, "PP-OCRv6");
        Directory.CreateDirectory(folder);

        string sizeTag = modelSize.ToString().ToLowerInvariant(); // tiny | small | medium

        string detFileName = $"PP-OCRv6_det_{sizeTag}.onnx";
        string recFileName = $"PP-OCRv6_rec_{sizeTag}.onnx";
        string dictFileName = $"ppocrv6_{sizeTag}_dict.txt";

        string detPath = Path.Combine(folder, detFileName);
        string recPath = Path.Combine(folder, recFileName);
        string dictPath = Path.Combine(folder, dictFileName);
        string clsPath = Path.Combine(folder, ClsFileName);

        // En el origen (default_models.yaml), el diccionario de "small" y "medium" se llama
        // igual para ambos ("ppocrv6_dict.txt", en carpetas distintas); se renombra al guardar
        // localmente para no pisar uno con el otro. "tiny" ya tiene nombre unico en el origen.
        string dictRemoteFileName = modelSize == OcrModelSize.Tiny ? "ppocrv6_tiny_dict.txt" : "ppocrv6_dict.txt";

        var filesToDownload = new List<(string DestinationPath, string Url)>
        {
            (detPath, $"{ModelScopeBaseUrl}/onnx/PP-OCRv6/det/{detFileName}"),
            (recPath, $"{ModelScopeBaseUrl}/onnx/PP-OCRv6/rec/{recFileName}"),
            (dictPath, $"{ModelScopeBaseUrl}/paddle/PP-OCRv6/rec/PP-OCRv6_rec_{sizeTag}/{dictRemoteFileName}"),
            (clsPath, $"{ModelScopeBaseUrl}/onnx/PP-OCRv5/cls/{ClsFileName}"),
        };

        foreach (var (destinationPath, url) in filesToDownload)
        {
            if (File.Exists(destinationPath))
                continue;

            Console.WriteLine($"[RapidOcrModelManager] Descargando {Path.GetFileName(destinationPath)}...");
            await DownloadFileAsync(url, destinationPath, cancellationToken);
            Console.WriteLine($"[RapidOcrModelManager] Listo: {destinationPath}");
        }

        return new RapidOcrModelPaths(detPath, clsPath, recPath, dictPath);
    }

    // Descarga un archivo temporal primero y al final lo renombra
    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        string tempPath = destinationPath + ".tmp";

        using HttpResponseMessage response = await _httpClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (Stream httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (FileStream fileStream = File.Create(tempPath))
        {
            await httpStream.CopyToAsync(fileStream, cancellationToken);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }
}
