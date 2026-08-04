namespace Neni.Ocr.Services;

public enum RapidOcrVersion
{
    V5,
    V6
}

public sealed record RapidOcrModelPaths(
    string DetectionModelPath,
    string ClassificationModelPath,
    string RecognitionModelPath,
    string DictionaryPath);

public sealed class RapidOcrModelManagerService
{
    private const string ModelScopeBaseUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.8.0";
    
    // Nombres de archivo para PP-OCRv5
    private const string V5_DetFileName = "ch_PP-OCRv5_det_mobile.onnx";
    private const string V5_ClsFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    private const string V5_RecFileName = "ch_PP-OCRv5_rec_mobile.onnx";
    private const string V5_DictFileName = "ppocrv5_dict.txt";

    private readonly string _modelsRootPath;
    private readonly HttpClient _httpClient;

    public RapidOcrModelManagerService(string modelsRootPath = "Models", HttpClient httpClient = null)
    {
        _modelsRootPath = modelsRootPath;
        _httpClient = httpClient;
    }
    
    // Verifica que los modelos de la version solicitada existe en disco
    public async Task<RapidOcrModelPaths> DetectAsync(RapidOcrVersion rapidOcrVersion,
        CancellationToken cancellationToken = default)
    {
        return rapidOcrVersion switch
        {
            RapidOcrVersion.V5 => await DetectV5Async(cancellationToken),
            RapidOcrVersion.V6 => throw new NotSupportedException(
                "PP-OCRv6 todavia no esta publicada en el repo de RapidAI/RapidOCR" +
                "(default_models.yaml). Actualiza esta clase con los nombres de archivo" +
                "y URLs cuando esten disponibles"),
            _ => throw new ArgumentOutOfRangeException(nameof(rapidOcrVersion), rapidOcrVersion, null)
        };
    }

    // Detecta que la v5 de RapidOcr este en el disco no la descarga y retorna la rutas
    private async Task<RapidOcrModelPaths> DetectV5Async(CancellationToken cancellationToken)
    {
        string folder = Path.Combine(_modelsRootPath, "PP-OCRv5");
        
        string detPath = Path.Combine(folder, V5_DetFileName);
        string clsPath = Path.Combine(folder, V5_ClsFileName);
        string recPath = Path.Combine(folder, V5_RecFileName);
        string dictPath = Path.Combine(folder, V5_DictFileName);
        
        bool allModelsExist =
            File.Exists(detPath) &&
            File.Exists(clsPath) &&
            File.Exists(recPath) &&
            File.Exists(dictPath);

        if (!allModelsExist)
            await DownloadOnnxPPOCRv5Async(folder, cancellationToken);
        
        return new RapidOcrModelPaths(detPath, clsPath, recPath, dictPath);
    }
    
    // Descarga los 4 archivos necesarios de PP-OCRv5
    private async Task DownloadOnnxPPOCRv5Async(string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);

        var filesToDownload = new List<(string Filename, string path)>
        {
            (V5_DetFileName, $"{ModelScopeBaseUrl}/onnx/PP-OCRv5/det/{V5_DetFileName}"),
            (V5_ClsFileName, $"{ModelScopeBaseUrl}/onnx/PP-OCRv5/cls/{V5_ClsFileName}"),
            (V5_RecFileName, $"{ModelScopeBaseUrl}/onnx/PP-OCRv5/rec/{V5_RecFileName}"),
            (V5_DictFileName,
                $"{ModelScopeBaseUrl}/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/{V5_DictFileName}"),
        };

        foreach (var (filename, url) in filesToDownload)
        {
            string destinationPath = Path.Combine(folder, filename);
            
            if (File.Exists(destinationPath))
                continue;
            
            Console.WriteLine($"[RapidOcrModelManager] Descargando {filename}...");
            await DownloadFileAsync(url, destinationPath, cancellationToken);
            Console.WriteLine($"[RapidOcrModelManager] Listo: {destinationPath}");
        }
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
        
        File.Move(tempPath, destinationPath, overwrite:true);
    }
}