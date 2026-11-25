using FaceRecognition.Models;
using OpenCvSharp;
using OpenCvSharp.Face;

namespace FaceRecognition.Services;

/// <summary>
/// LBPH (Local Binary Patterns Histograms) algoritması ile yüz tanıma.
/// Veri setindeki etiketli yüz örnekleriyle eğitilir ve yeni bir yüzün kime ait olduğunu tahmin eder.
/// </summary>
public sealed class FaceRecognizerService : IDisposable
{
    private LBPHFaceRecognizer? _model;
    private readonly List<string> _labelNames = [];

    /// <summary>Bu uzaklığın üstündeki tahminler "Bilinmiyor" kabul edilir.</summary>
    public double UnknownThreshold { get; set; } = 75;

    public bool IsTrained => _model != null;
    public int LabelCount => _labelNames.Count;
    public int SampleCount { get; private set; }

    /// <summary>
    /// Modeli verilen örneklerle eğitir. En az iki farklı etiket gerekir (ödev şartı).
    /// </summary>
    public void Train(IReadOnlyList<(string Label, Mat Face)> samples)
    {
        var labels = samples.Select(s => s.Label).Distinct().OrderBy(l => l).ToList();
        if (labels.Count < 2)
            throw new FaceAppException(
                "Eğitim için en az iki farklı etiket gerekir. Her etiket için birkaç yüz örneği ekleyin.");

        var model = LBPHFaceRecognizer.Create(radius: 1, neighbors: 8, gridX: 8, gridY: 8);
        try
        {
            var images = samples.Select(s => s.Face).ToArray();
            var ids = samples.Select(s => labels.IndexOf(s.Label)).ToArray();
            model.Train(images, ids);
        }
        catch (OpenCVException ex)
        {
            model.Dispose();
            throw new FaceAppException($"Model eğitilemedi: {ex.Message}", ex);
        }

        _model?.Dispose();
        _model = model;
        _labelNames.Clear();
        _labelNames.AddRange(labels);
        SampleCount = samples.Count;
    }

    /// <summary>Modeli sıfırlar (örneğin etiketler silindiğinde).</summary>
    public void Reset()
    {
        _model?.Dispose();
        _model = null;
        _labelNames.Clear();
        SampleCount = 0;
    }

    /// <summary>Hazırlanmış (gri, standart boyutlu) yüz için tahmin yapar.</summary>
    public FaceResult Predict(Mat preparedFace, Rect region)
    {
        if (_model == null)
            return new FaceResult(region);

        _model.Predict(preparedFace, out int id, out double distance);

        // Uzaklığı yaklaşık bir güven yüzdesine çevir: 0 uzaklık = %100, 2 * eşik = %0
        double confidence = Math.Clamp(100 - distance / (UnknownThreshold * 2) * 100, 0, 100);

        string? label = id >= 0 && id < _labelNames.Count && distance <= UnknownThreshold
            ? _labelNames[id]
            : null;

        return new FaceResult(region, label, distance, confidence);
    }

    public void Dispose() => Reset();
}
