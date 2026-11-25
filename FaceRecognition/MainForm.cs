using System.Diagnostics;
using FaceRecognition.Models;
using FaceRecognition.Services;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace FaceRecognition;

/// <summary>
/// Ana pencere: kamera / resim görüntüsü, algılanan yüzler, eğitim ve veri ekleme bölümü.
/// </summary>
public class MainForm : Form
{
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMH219-FaceRecognition");

    // --- Servisler ---
    private readonly CameraService _camera = new();
    private readonly FaceRecognizerService _recognizer = new();
    private FaceDetector? _detector;
    private FrameProcessor? _processor;
    private FaceDataStore? _store;

    /// <summary>Kamera iş parçacığı ile arayüz arasında paylaşılan verileri korur.</summary>
    private readonly object _sync = new();
    private Mat? _currentFrame;                 // Son işlenen ham görüntü (örnek eklemek için)
    private IReadOnlyList<FaceResult> _currentFaces = [];
    private volatile bool _uiUpdatePending;     // Arayüz yetişemezse kare atlanır

    // --- Kontroller ---
    private readonly PictureBox _picture;
    private readonly Label _lblActivity;
    private readonly ToolStripStatusLabel _status;
    private readonly ToolStripStatusLabel _fps;
    private Button _btnStartCamera = null!;
    private Button _btnStopCamera = null!;
    private NumericUpDown _numCamera = null!;
    private CheckBox _chkRecognize = null!;
    private ListView _lvFaces = null!;
    private TextBox _txtLabel = null!;
    private ListView _lvLabels = null!;
    private NumericUpDown _numThreshold = null!;
    private Button _btnAddSample = null!;
    private Button _btnBurst = null!;

    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private int _frameCounter;

    public MainForm()
    {
        Text = "YMH219 - OpenCV Yüz Algılama ve Tanıma";
        ClientSize = new System.Drawing.Size(1240, 760);
        MinimumSize = new System.Drawing.Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(242, 244, 248);
        Font = new Font("Segoe UI", 9.5f);

        _lblActivity = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White
        };

        var statusStrip = new StatusStrip();
        _status = new ToolStripStatusLabel("Hazır") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _fps = new ToolStripStatusLabel("");
        statusStrip.Items.AddRange([_status, _fps]);

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(25, 28, 35)
        };
        _picture.Paint += Picture_Paint;

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_picture, 0, 0);
        left.Controls.Add(BuildCameraButtons(), 0, 1);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        main.Controls.Add(left, 0, 0);
        main.Controls.Add(BuildSidePanel(), 1, 0);

        Controls.Add(main);
        Controls.Add(_lblActivity);
        Controls.Add(statusStrip);

        _camera.FrameCaptured += Camera_FrameCaptured;
        _camera.CameraError += (_, ex) => BeginInvokeSafe(() =>
        {
            StopCamera();
            ShowError(ex.Message);
        });

        UpdateUiState();
    }

    // =====================================================================
    // Arayüz oluşturma
    // =====================================================================

    private Control BuildCameraButtons()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        _btnStartCamera = MakeButton("📷 Kamera Başlat", (_, _) => StartCamera(), 140, Color.FromArgb(40, 140, 80));
        _btnStopCamera = MakeButton("■ Kamera Durdur", (_, _) => StopCamera(), 130, Color.FromArgb(190, 55, 55));
        var btnOpen = MakeButton("🖼 Görüntü Seç", (_, _) => OpenImage(), 125);
        var btnSaveFaces = MakeButton("💾 Yüzleri Kaydet", (_, _) => SaveDetectedFaces(), 135);

        _numCamera = new NumericUpDown { Minimum = 0, Maximum = 5, Width = 45, Margin = new Padding(4, 8, 10, 0) };
        _chkRecognize = new CheckBox
        {
            Text = "Tanıma açık", Checked = true, AutoSize = true, Margin = new Padding(6, 9, 0, 0)
        };
        _chkRecognize.CheckedChanged += (_, _) =>
        {
            if (_processor != null) _processor.RecognitionEnabled = _chkRecognize.Checked;
            ReprocessStillImage();
        };

        row.Controls.AddRange([
            _btnStartCamera, _btnStopCamera, new Label { Text = "Kamera no:", AutoSize = true, Margin = new Padding(4, 10, 0, 0) },
            _numCamera, btnOpen, btnSaveFaces, _chkRecognize
        ]);
        return row;
    }

    private Control BuildSidePanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 8, 8, 8) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

        // --- Algılanan yüzler ---
        var facesGroup = MakeGroup("Algılanan Yüzler");
        _lvFaces = MakeListView();
        _lvFaces.Columns.Add("#", 32);
        _lvFaces.Columns.Add("Tahmin (etiket)", 135);
        _lvFaces.Columns.Add("Güven", 62, HorizontalAlignment.Right);
        _lvFaces.Columns.Add("Uzaklık", 62, HorizontalAlignment.Right);
        _lvFaces.Columns.Add("Boyut", 60);
        facesGroup.Controls.Add(_lvFaces);

        // --- Eğitim ---
        var trainGroup = MakeGroup("Eğitim / Veri Ekleme");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Font = Font, ForeColor = Color.Black };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var labelRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _txtLabel = new TextBox { Width = 200, PlaceholderText = "Etiket adı (örn. Ömer)", Margin = new Padding(0, 4, 4, 4) };
        labelRow.Controls.AddRange([new Label { Text = "Etiket:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, _txtLabel]);

        var addRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _btnAddSample = MakeButton("➕ Yüzü Örneğe Ekle", async (_, _) => await AddSamplesAsync(1), 160, Color.FromArgb(45, 95, 170));
        _btnBurst = MakeButton("📸 10 Örnek Çek", async (_, _) => await AddSamplesAsync(10), 130);
        addRow.Controls.AddRange([_btnAddSample, _btnBurst]);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(350, 0),
            ForeColor = Color.DimGray,
            Text = "Görüntüdeki en büyük yüz seçili etikete eklenir. Her etiket için farklı açılardan " +
                   "5-10 örnek önerilir. En az 2 etiket olunca model otomatik eğitilir."
        };

        _lvLabels = MakeListView();
        _lvLabels.Columns.Add("Etiket", 200);
        _lvLabels.Columns.Add("Örnek sayısı", 100, HorizontalAlignment.Right);
        _lvLabels.SelectedIndexChanged += (_, _) =>
        {
            if (_lvLabels.SelectedItems.Count > 0) _txtLabel.Text = _lvLabels.SelectedItems[0].Text;
        };

        var thresholdRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _numThreshold = new NumericUpDown
        {
            Minimum = 20, Maximum = 200, Value = (decimal)_recognizer.UnknownThreshold, Width = 60, Margin = new Padding(4, 6, 4, 0)
        };
        _numThreshold.ValueChanged += (_, _) =>
        {
            lock (_sync) _recognizer.UnknownThreshold = (double)_numThreshold.Value;
            ReprocessStillImage();
        };
        thresholdRow.Controls.AddRange([
            new Label { Text = "Bilinmiyor eşiği (uzaklık):", AutoSize = true, Margin = new Padding(0, 9, 0, 0) },
            _numThreshold
        ]);

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actionRow.Controls.AddRange([
            MakeButton("🧠 Modeli Eğit", (_, _) => TrainModel(showMessage: true), 115),
            MakeButton("🗑 Etiketi Sil", (_, _) => DeleteSelectedLabel(), 105),
            MakeButton("📂 Klasör", (_, _) => OpenDataFolder(), 85)
        ]);

        t.Controls.Add(labelRow, 0, 0);
        t.Controls.Add(addRow, 0, 1);
        t.Controls.Add(hint, 0, 2);
        t.Controls.Add(_lvLabels, 0, 3);
        t.Controls.Add(thresholdRow, 0, 4);
        t.Controls.Add(actionRow, 0, 5);
        trainGroup.Controls.Add(t);

        panel.Controls.Add(facesGroup, 0, 0);
        panel.Controls.Add(trainGroup, 0, 1);
        return panel;
    }

    private static Button MakeButton(string text, EventHandler onClick, int width, Color? color = null)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 34, Margin = new Padding(0, 0, 6, 6),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = color ?? Color.White,
            ForeColor = color.HasValue ? Color.White : Color.FromArgb(35, 35, 35),
            Font = new Font("Segoe UI", 9.5f, color.HasValue ? FontStyle.Bold : FontStyle.Regular)
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(180, 185, 195);
        b.Click += onClick;
        if (color.HasValue)
        {
            Color active = color.Value;
            b.EnabledChanged += (_, _) => b.BackColor = b.Enabled ? active : Color.FromArgb(200, 203, 210);
        }
        return b;
    }

    private static GroupBox MakeGroup(string title) => new()
    {
        Text = title, Dock = DockStyle.Fill, Padding = new Padding(8),
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(45, 95, 170)
    };

    private static ListView MakeListView() => new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = false,
        HideSelection = false, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.Black
    };

    // =====================================================================
    // Başlatma
    // =====================================================================

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        InitializeServices();
    }

    private void InitializeServices()
    {
        try
        {
            string cascade = Path.Combine(AppContext.BaseDirectory, "Assets", "haarcascade_frontalface_default.xml");
            _detector = new FaceDetector(cascade);
            _processor = new FrameProcessor(_detector, _recognizer);
            _store = new FaceDataStore(Path.Combine(AppDataRoot, "FaceData"));
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
            SetStatus("Model yüklenemedi; algılama devre dışı.");
            UpdateUiState();
            return;
        }

        RefreshLabels();
        TrainModel(showMessage: false);
        SetStatus("Hazır. Kamerayı başlatın veya bir görüntü seçin.");
        UpdateUiState();
    }

    // =====================================================================
    // Kamera ve görüntü
    // =====================================================================

    private void StartCamera()
    {
        if (_processor == null) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            _camera.Start((int)_numCamera.Value);
            SetStatus($"Kamera {_numCamera.Value} açıldı.");
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
            UpdateUiState();
        }
    }

    private void StopCamera()
    {
        _camera.Stop();
        _fps.Text = "";
        SetStatus("Kamera durduruldu. Son kare ekranda kalır.");
        UpdateUiState();
    }

    /// <summary>Kamera iş parçacığında çalışır: kareyi işler, sonucu arayüze gönderir.</summary>
    private void Camera_FrameCaptured(object? sender, Mat frame)
    {
        if (_uiUpdatePending || _processor == null)
        {
            frame.Dispose();
            return;
        }

        Bitmap bitmap;
        IReadOnlyList<FaceResult> faces;
        try
        {
            lock (_sync)
            {
                faces = _processor.Process(frame);
                _currentFrame?.Dispose();
                _currentFrame = frame;
                _currentFaces = faces;
            }
            bitmap = BitmapConverter.ToBitmap(frame);
            FaceOverlayRenderer.Draw(bitmap, faces);
        }
        catch (OpenCVException)
        {
            // Tek bir bozuk kare uygulamayı durdurmasın
            lock (_sync)
            {
                if (!ReferenceEquals(_currentFrame, frame)) frame.Dispose();
            }
            return;
        }

        _uiUpdatePending = true;
        BeginInvokeSafe(() =>
        {
            ShowFrame(bitmap, faces);
            _uiUpdatePending = false;
            CountFps();
        }, onSkipped: bitmap.Dispose);
    }

    private void OpenImage()
    {
        if (_processor == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Yüz içeren bir görüntü seçin",
            Filter = "Görüntü dosyaları|*.jpg;*.jpeg;*.png;*.bmp|Tüm dosyalar|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (_camera.IsRunning) StopCamera();
        LoadStillImage(dialog.FileName);
    }

    /// <summary>Dosyadan görüntü yükler, işler ve gösterir.</summary>
    public void LoadStillImage(string path)
    {
        Mat image;
        try
        {
            image = Cv2.ImRead(path, ImreadModes.Color);
        }
        catch (OpenCVException ex)
        {
            ShowError($"Görüntü okunamadı: {ex.Message}");
            return;
        }

        if (image.Empty())
        {
            image.Dispose();
            ShowError("Seçilen dosya geçerli bir görüntü değil veya okunamadı.");
            return;
        }

        // Çok büyük fotoğrafları küçült (algılama hızı için)
        if (image.Width > 1280)
        {
            double f = 1280.0 / image.Width;
            Cv2.Resize(image, image, new OpenCvSharp.Size(), f, f, InterpolationFlags.Area);
        }

        lock (_sync)
        {
            _currentFrame?.Dispose();
            _currentFrame = image;
        }
        ReprocessStillImage();
        SetStatus($"Görüntü yüklendi: {Path.GetFileName(path)} - {_currentFaces.Count} yüz bulundu.");
    }

    /// <summary>Kamera kapalıyken ekrandaki görüntüyü (ayar değişince) yeniden işler.</summary>
    private void ReprocessStillImage()
    {
        if (_camera.IsRunning || _processor == null) return;
        Bitmap bitmap;
        IReadOnlyList<FaceResult> faces;
        lock (_sync)
        {
            if (_currentFrame == null) return;
            faces = _processor.Process(_currentFrame);
            _currentFaces = faces;
            bitmap = BitmapConverter.ToBitmap(_currentFrame);
        }
        FaceOverlayRenderer.Draw(bitmap, faces);
        ShowFrame(bitmap, faces);
    }

    private void ShowFrame(Bitmap bitmap, IReadOnlyList<FaceResult> faces)
    {
        var old = _picture.Image;
        _picture.Image = bitmap;
        old?.Dispose();
        UpdateFaceList(faces);
        UpdateActivity();
    }

    private void UpdateFaceList(IReadOnlyList<FaceResult> faces)
    {
        _lvFaces.BeginUpdate();
        _lvFaces.Items.Clear();
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            var item = new ListViewItem([
                (i + 1).ToString(),
                f.Distance is null ? "(tanıma yok)" : f.Label,
                f.Distance is null ? "-" : $"%{f.Confidence:0}",
                f.Distance is null ? "-" : $"{f.Distance:0.0}",
                $"{f.Region.Width}x{f.Region.Height}"
            ]);
            if (f.Distance != null)
                item.ForeColor = f.IsKnown ? Color.ForestGreen : Color.DarkOrange;
            _lvFaces.Items.Add(item);
        }
        _lvFaces.EndUpdate();
    }

    private void Picture_Paint(object? sender, PaintEventArgs e)
    {
        if (_picture.Image != null) return;
        TextRenderer.DrawText(e.Graphics,
            "Görüntü yok\n\n📷 Kamera Başlat veya 🖼 Görüntü Seç",
            new Font("Segoe UI", 14f), _picture.ClientRectangle, Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private void CountFps()
    {
        _frameCounter++;
        if (_fpsWatch.ElapsedMilliseconds < 1000) return;
        _fps.Text = $"{_frameCounter * 1000.0 / _fpsWatch.ElapsedMilliseconds:0} FPS";
        _frameCounter = 0;
        _fpsWatch.Restart();
    }

    // =====================================================================
    // Eğitim / veri ekleme
    // =====================================================================

    /// <summary>
    /// Mevcut görüntüdeki en büyük yüzü etikete ekler. count > 1 ise kameradan aralıklarla birden fazla örnek alır.
    /// </summary>
    private async Task AddSamplesAsync(int count)
    {
        if (_store == null || _processor == null) return;

        string label;
        try
        {
            label = FaceDataStore.NormalizeLabel(_txtLabel.Text);
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
            _txtLabel.Focus();
            return;
        }

        if (count > 1 && !_camera.IsRunning)
        {
            ShowError("Birden fazla örnek çekmek için önce kamerayı başlatın.");
            return;
        }

        _btnAddSample.Enabled = _btnBurst.Enabled = false;
        int added = 0;
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (TryAddCurrentFace(label)) added++;
                SetStatus($"'{label}' için örnek ekleniyor... {added}/{count}");
                if (count > 1) await Task.Delay(350); // Farklı pozlar için kısa bekleme
            }
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _btnAddSample.Enabled = _btnBurst.Enabled = true;
        }

        if (added == 0)
        {
            ShowError("Görüntüde yüz bulunamadı. Yüzünüzün kameraya dönük ve iyi aydınlatılmış olduğundan emin olun.");
            return;
        }

        RefreshLabels();
        TrainModel(showMessage: false);
        ReprocessStillImage();
        SetStatus($"'{label}' etiketine {added} örnek eklendi.");
    }

    private bool TryAddCurrentFace(string label)
    {
        lock (_sync)
        {
            if (_currentFrame == null || _processor == null || _store == null) return false;
            var face = _processor.FindLargestFace(_currentFrame);
            if (face == null) return false;
            using var prepared = FaceDetector.PrepareFace(_currentFrame, face.Value);
            _store.AddSample(label, prepared);
            return true;
        }
    }

    /// <summary>Veri setindeki tüm örneklerle modeli yeniden eğitir.</summary>
    private void TrainModel(bool showMessage)
    {
        if (_store == null) return;

        var samples = _store.LoadAll();
        try
        {
            lock (_sync)
            {
                if (samples.Select(s => s.Label).Distinct().Count() < 2)
                {
                    _recognizer.Reset();
                    if (showMessage)
                        ShowError("Eğitim için en az iki farklı etiket gerekir. Şu an " +
                                  $"{samples.Select(s => s.Label).Distinct().Count()} etiket var.");
                    return;
                }
                _recognizer.Train(samples);
            }
            SetStatus($"Model eğitildi: {_recognizer.LabelCount} etiket, {_recognizer.SampleCount} örnek.");
            if (showMessage)
                MessageBox.Show(this, $"Model eğitildi.\n{_recognizer.LabelCount} etiket, {_recognizer.SampleCount} örnek.",
                    "Eğitim", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            foreach (var s in samples) s.Face.Dispose();
            UpdateActivity();
        }
    }

    private void RefreshLabels()
    {
        if (_store == null) return;
        _lvLabels.BeginUpdate();
        _lvLabels.Items.Clear();
        foreach (var label in _store.GetLabels())
            _lvLabels.Items.Add(new ListViewItem([label.Name, label.SampleCount.ToString()]));
        _lvLabels.EndUpdate();
    }

    private void DeleteSelectedLabel()
    {
        if (_store == null || _lvLabels.SelectedItems.Count == 0)
        {
            ShowError("Silmek için listeden bir etiket seçin.");
            return;
        }

        string label = _lvLabels.SelectedItems[0].Text;
        if (MessageBox.Show(this, $"'{label}' etiketi ve tüm örnekleri silinsin mi?", "Etiketi Sil",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        try
        {
            _store.DeleteLabel(label);
        }
        catch (FaceAppException ex)
        {
            ShowError(ex.Message);
            return;
        }
        RefreshLabels();
        TrainModel(showMessage: false);
        ReprocessStillImage();
        SetStatus($"'{label}' etiketi silindi.");
    }

    private void OpenDataFolder()
    {
        if (_store == null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.RootFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError($"Klasör açılamadı: {ex.Message}");
        }
    }

    /// <summary>Ekranda algılanan tüm yüzleri ayrı dosyalar olarak kaydeder (isteğe bağlı özellik).</summary>
    private void SaveDetectedFaces()
    {
        string folder = Path.Combine(AppDataRoot, "SavedFaces", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        int saved = 0;
        try
        {
            lock (_sync)
            {
                if (_currentFrame == null || _currentFaces.Count == 0)
                {
                    ShowError("Kaydedilecek algılanmış yüz yok.");
                    return;
                }
                Directory.CreateDirectory(folder);
                foreach (var face in _currentFaces)
                {
                    var r = face.Region.Intersect(new Rect(0, 0, _currentFrame.Width, _currentFrame.Height));
                    using var crop = new Mat(_currentFrame, r);
                    Cv2.ImWrite(Path.Combine(folder, $"{++saved}_{face.Label}.png"), crop);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenCVException)
        {
            ShowError($"Yüzler kaydedilemedi: {ex.Message}");
            return;
        }
        SetStatus($"{saved} yüz kaydedildi: {folder}");
    }

    // =====================================================================
    // Yardımcılar
    // =====================================================================

    private void UpdateUiState()
    {
        bool ready = _processor != null;
        _btnStartCamera.Enabled = ready && !_camera.IsRunning;
        _btnStopCamera.Enabled = _camera.IsRunning;
        _numCamera.Enabled = !_camera.IsRunning;
        _btnBurst.Enabled = ready;
        _btnAddSample.Enabled = ready;
        UpdateActivity();
    }

    private void UpdateActivity()
    {
        string camera = _camera.IsRunning ? "📷 Kamera açık" : "Kamera kapalı";
        string model = _recognizer.IsTrained
            ? $"🧠 Model eğitildi ({_recognizer.LabelCount} etiket, {_recognizer.SampleCount} örnek)"
            : "Model eğitilmedi (yalnızca algılama)";
        _lblActivity.Text = $"{camera}   |   {model}   |   {_currentFaces.Count} yüz";
        _lblActivity.BackColor = _camera.IsRunning ? Color.FromArgb(40, 140, 80) : Color.FromArgb(95, 100, 115);
    }

    private void SetStatus(string message) => _status.Text = $"{DateTime.Now:HH:mm:ss}  {message}";

    private void ShowError(string message)
    {
        SetStatus("Hata: " + message.Split('\n')[0]);
        MessageBox.Show(this, message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void BeginInvokeSafe(Action action, Action? onSkipped = null)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            onSkipped?.Invoke();
            return;
        }
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { onSkipped?.Invoke(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _camera.FrameCaptured -= Camera_FrameCaptured;
        _camera.Dispose();
        lock (_sync)
        {
            _recognizer.Dispose();
            _detector?.Dispose();
            _currentFrame?.Dispose();
        }
        base.OnFormClosing(e);
    }
}
