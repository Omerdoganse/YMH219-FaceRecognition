namespace FaceRecognition.Models;

/// <summary>
/// Veri setindeki bir etiket (kişi) ve o etikete ait örnek sayısı.
/// </summary>
/// <param name="Name">Etiket adı (örn. "Ömer").</param>
/// <param name="SampleCount">Kayıtlı yüz örneği sayısı.</param>
public sealed record LabelInfo(string Name, int SampleCount);
