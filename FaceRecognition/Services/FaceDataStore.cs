using FaceRecognition.Models;
using OpenCvSharp;

namespace FaceRecognition.Services;

/// <summary>
/// Etiketli yüz örneklerini diskte saklar.
/// Yapı: FaceData/&lt;etiket&gt;/&lt;zaman&gt;.png  (her örnek gri, 120x120 yüz görüntüsü)
/// </summary>
public sealed class FaceDataStore
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public string RootFolder { get; }

    public FaceDataStore(string rootFolder)
    {
        RootFolder = rootFolder;
        try
        {
            Directory.CreateDirectory(RootFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FaceAppException($"Veri seti klasörü oluşturulamadı: {RootFolder}", ex);
        }
    }

    /// <summary>Etiket adını doğrular ve klasör adı olarak güvenli hale getirir.</summary>
    public static string NormalizeLabel(string label)
    {
        label = label.Trim();
        if (label.Length is 0 or > 30)
            throw new FaceAppException("Etiket adı 1 ile 30 karakter arasında olmalıdır.");
        if (label.IndexOfAny(InvalidChars) >= 0)
            throw new FaceAppException("Etiket adında \\ / : * ? \" < > | gibi karakterler kullanılamaz.");
        if (label.Equals(FaceResult.UnknownLabel, StringComparison.OrdinalIgnoreCase))
            throw new FaceAppException($"'{FaceResult.UnknownLabel}' adı ayrılmıştır, başka bir ad seçin.");
        return label;
    }

    /// <summary>Hazırlanmış yüz görüntüsünü ilgili etiketin klasörüne kaydeder.</summary>
    public string AddSample(string label, Mat preparedFace)
    {
        label = NormalizeLabel(label);
        string folder = Path.Combine(RootFolder, label);
        string path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        try
        {
            Directory.CreateDirectory(folder);
            if (!Cv2.ImWrite(path, preparedFace))
                throw new FaceAppException("Yüz örneği dosyaya yazılamadı.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenCVException)
        {
            throw new FaceAppException($"Yüz örneği kaydedilemedi: {ex.Message}", ex);
        }
        return path;
    }

    /// <summary>Etiketleri ve örnek sayılarını listeler.</summary>
    public IReadOnlyList<LabelInfo> GetLabels() =>
        Directory.GetDirectories(RootFolder)
                 .Select(dir => new LabelInfo(Path.GetFileName(dir), Directory.GetFiles(dir, "*.png").Length))
                 .Where(l => l.SampleCount > 0)
                 .OrderBy(l => l.Name)
                 .ToList();

    /// <summary>
    /// Tüm örnekleri belleğe yükler. Dönen Mat nesneleri çağıran tarafından Dispose edilmelidir.
    /// </summary>
    public List<(string Label, Mat Face)> LoadAll()
    {
        var samples = new List<(string, Mat)>();
        foreach (var label in GetLabels())
        {
            foreach (var file in Directory.GetFiles(Path.Combine(RootFolder, label.Name), "*.png"))
            {
                var img = Cv2.ImRead(file, ImreadModes.Grayscale);
                if (img.Empty()) { img.Dispose(); continue; } // Bozuk dosyaları atla
                if (img.Size() != FaceDetector.FaceSize)
                    Cv2.Resize(img, img, FaceDetector.FaceSize);
                samples.Add((label.Name, img));
            }
        }
        return samples;
    }

    /// <summary>Bir etiketi ve tüm örneklerini siler.</summary>
    public void DeleteLabel(string label)
    {
        string folder = Path.Combine(RootFolder, NormalizeLabel(label));
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FaceAppException($"Etiket silinemedi: {ex.Message}", ex);
        }
    }
}
