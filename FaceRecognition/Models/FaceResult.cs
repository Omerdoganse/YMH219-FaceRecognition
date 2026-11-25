using OpenCvSharp;

namespace FaceRecognition.Models;

/// <summary>
/// Bir karede bulunan tek bir yüzün algılama ve tanıma sonucu.
/// </summary>
public sealed class FaceResult
{
    /// <summary>Etiket bulunamadığında gösterilecek ad.</summary>
    public const string UnknownLabel = "Bilinmiyor";

    /// <summary>Yüzün görüntü üzerindeki konumu.</summary>
    public Rect Region { get; }

    /// <summary>Tahmin edilen etiket (tanıma yapılmadıysa veya eşiği geçemediyse "Bilinmiyor").</summary>
    public string Label { get; }

    /// <summary>LBPH uzaklık değeri (küçük = daha benzer). Tanıma yapılmadıysa null.</summary>
    public double? Distance { get; }

    /// <summary>Uzaklık değerinden hesaplanan 0-100 arası güven skoru.</summary>
    public double Confidence { get; }

    public bool IsKnown => Label != UnknownLabel;

    public FaceResult(Rect region, string? label = null, double? distance = null, double confidence = 0)
    {
        Region = region;
        Label = string.IsNullOrWhiteSpace(label) ? UnknownLabel : label;
        Distance = distance;
        Confidence = Math.Clamp(confidence, 0, 100);
    }

    /// <summary>Görüntü üzerine yazılacak kısa metin (örn. "Ömer %82").</summary>
    public string Caption => Distance is null ? "Yüz" : $"{Label} %{Confidence:0}";
}
