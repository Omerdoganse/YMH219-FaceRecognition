using OpenCvSharp;

namespace FaceRecognition.Services;

/// <summary>
/// Kameradan arka planda sürekli kare okuyan servis.
/// Her yeni kare FrameCaptured olayı ile bildirilir.
/// </summary>
public sealed class CameraService : IDisposable
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>
    /// Yeni kare yakalandığında tetiklenir. Olayı dinleyen taraf Mat nesnesini Dispose etmelidir.
    /// Arka plan iş parçacığından çağrılır.
    /// </summary>
    public event EventHandler<Mat>? FrameCaptured;

    /// <summary>Kamera bağlantısı beklenmedik şekilde koptuğunda tetiklenir.</summary>
    public event EventHandler<FaceAppException>? CameraError;

    /// <summary>Belirtilen kamerayı açar ve kare okumaya başlar.</summary>
    public void Start(int cameraIndex = 0)
    {
        if (IsRunning) return;

        var capture = new VideoCapture();
        bool opened;
        try
        {
            opened = capture.Open(cameraIndex, VideoCaptureAPIs.DSHOW);
        }
        catch (OpenCVException ex)
        {
            capture.Dispose();
            throw new FaceAppException($"Kamera açılırken hata oluştu: {ex.Message}", ex);
        }

        if (!opened || !capture.IsOpened())
        {
            capture.Dispose();
            throw new FaceAppException(
                $"Kamera {cameraIndex} açılamadı.\n\nKameranın bağlı olduğundan, başka bir uygulama tarafından " +
                "kullanılmadığından ve Windows Gizlilik ayarlarında kamera erişimine izin verildiğinden emin olun.");
        }

        capture.Set(VideoCaptureProperties.FrameWidth, 640);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);

        _capture = capture;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => CaptureLoop(capture, token), token);
    }

    private void CaptureLoop(VideoCapture capture, CancellationToken token)
    {
        int emptyFrames = 0;
        while (!token.IsCancellationRequested)
        {
            var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                if (++emptyFrames > 50) // ~2.5 sn boyunca görüntü yok
                {
                    CameraError?.Invoke(this, new FaceAppException("Kameradan görüntü alınamıyor. Bağlantı kopmuş olabilir."));
                    return;
                }
                Thread.Sleep(50);
                continue;
            }

            emptyFrames = 0;
            Cv2.Flip(frame, frame, FlipMode.Y); // Ayna görüntüsü: kullanıcıya daha doğal gelir
            FrameCaptured?.Invoke(this, frame);
            Thread.Sleep(30); // ~30 FPS üst sınır
        }
    }

    /// <summary>Kare okumayı durdurur ve kamerayı serbest bırakır.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1000); }
        catch (AggregateException) { }

        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();
}
