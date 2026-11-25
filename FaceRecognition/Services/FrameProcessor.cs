using FaceRecognition.Models;
using OpenCvSharp;

namespace FaceRecognition.Services;

/// <summary>
/// Bir görüntüdeki tüm yüzleri bulur ve (model eğitilmişse) her birini tanır.
/// Algılama ve tanıma servislerini bir araya getirir.
/// </summary>
public sealed class FrameProcessor(FaceDetector detector, FaceRecognizerService recognizer)
{
    /// <summary>false ise yalnızca algılama yapılır, tanıma atlanır.</summary>
    public bool RecognitionEnabled { get; set; } = true;

    public IReadOnlyList<FaceResult> Process(Mat image)
    {
        var faces = detector.Detect(image);
        var results = new List<FaceResult>(faces.Length);

        foreach (var region in faces.OrderBy(r => r.X))
        {
            if (!RecognitionEnabled || !recognizer.IsTrained)
            {
                results.Add(new FaceResult(region));
                continue;
            }

            using var prepared = FaceDetector.PrepareFace(image, region);
            results.Add(recognizer.Predict(prepared, region));
        }
        return results;
    }

    /// <summary>Görüntüdeki en büyük yüzü bulur (örnek eklerken kullanılır).</summary>
    public Rect? FindLargestFace(Mat image)
    {
        var faces = detector.Detect(image);
        return faces.Length == 0 ? null : faces.MaxBy(f => f.Width * f.Height);
    }
}
