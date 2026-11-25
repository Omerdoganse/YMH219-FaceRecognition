namespace FaceRecognition.Services;

/// <summary>
/// Kamera, model veya veri seti işlemlerinde oluşan ve kullanıcıya
/// doğrudan gösterilebilecek anlaşılır Türkçe mesaj taşıyan hata türü.
/// </summary>
public class FaceAppException : Exception
{
    public FaceAppException(string message) : base(message) { }

    public FaceAppException(string message, Exception inner) : base(message, inner) { }
}
