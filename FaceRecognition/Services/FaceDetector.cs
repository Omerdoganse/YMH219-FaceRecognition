using OpenCvSharp;

namespace FaceRecognition.Services;

/// <summary>
/// OpenCV Haar Cascade sınıflandırıcısı ile görüntüdeki yüzleri bulur.
/// </summary>
public sealed class FaceDetector : IDisposable
{
    /// <summary>Tanıma için yüzlerin dönüştürüldüğü standart boyut.</summary>
    public static readonly OpenCvSharp.Size FaceSize = new(120, 120);

    private readonly CascadeClassifier _cascade;

    /// <summary>Bulunacak en küçük yüz boyutu (piksel).</summary>
    public int MinFaceSize { get; set; } = 60;

    public FaceDetector(string cascadePath)
    {
        if (!File.Exists(cascadePath))
            throw new FaceAppException(
                $"Yüz algılama modeli bulunamadı:\n{cascadePath}\n\n" +
                "Assets klasöründeki haarcascade_frontalface_default.xml dosyasının çıktı klasörüne kopyalandığından emin olun.");

        try
        {
            _cascade = new CascadeClassifier(cascadePath);
        }
        catch (Exception ex) when (ex is OpenCVException or TypeInitializationException or DllNotFoundException)
        {
            throw new FaceAppException($"OpenCV başlatılamadı veya model dosyası okunamadı: {ex.Message}", ex);
        }

        if (_cascade.Empty())
        {
            _cascade.Dispose();
            throw new FaceAppException("Yüz algılama modeli bozuk veya geçersiz.");
        }
    }

    /// <summary>Renkli (BGR) görüntüdeki yüzlerin dikdörtgenlerini döndürür.</summary>
    public Rect[] Detect(Mat image)
    {
        if (image.Empty()) return [];

        using var gray = ToGray(image);
        Cv2.EqualizeHist(gray, gray); // Işık farklarını azaltır
        return _cascade.DetectMultiScale(
            gray,
            scaleFactor: 1.1,
            minNeighbors: 6,
            flags: HaarDetectionTypes.ScaleImage,
            minSize: new OpenCvSharp.Size(MinFaceSize, MinFaceSize));
    }

    /// <summary>
    /// Yüz bölgesini keser, griye çevirir ve tanıma için standart boyuta getirir.
    /// Dönen Mat nesnesini çağıran taraf Dispose etmelidir.
    /// </summary>
    public static Mat PrepareFace(Mat image, Rect region)
    {
        var safe = region.Intersect(new Rect(0, 0, image.Width, image.Height));
        using var crop = new Mat(image, safe);
        using var gray = ToGray(crop);
        var resized = new Mat();
        Cv2.Resize(gray, resized, FaceSize);
        Cv2.EqualizeHist(resized, resized);
        return resized;
    }

    private static Mat ToGray(Mat image)
    {
        var gray = new Mat();
        if (image.Channels() == 1) image.CopyTo(gray);
        else Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public void Dispose() => _cascade.Dispose();
}
