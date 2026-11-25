using System.Drawing.Drawing2D;
using FaceRecognition.Models;

namespace FaceRecognition;

/// <summary>
/// Yüz dikdörtgenlerini ve etiketleri GDI+ ile görüntünün üzerine çizer.
/// (OpenCV'nin PutText metodu Türkçe karakterleri desteklemediği için GDI+ kullanılır.)
/// </summary>
internal static class FaceOverlayRenderer
{
    private static readonly Color KnownColor = Color.FromArgb(40, 200, 90);
    private static readonly Color UnknownColor = Color.FromArgb(240, 150, 30);
    private static readonly Color DetectOnlyColor = Color.FromArgb(60, 160, 255);

    public static void Draw(Bitmap bitmap, IReadOnlyList<FaceResult> faces)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float scale = Math.Max(1f, bitmap.Width / 640f);
        using var font = new Font("Segoe UI", 11f * scale, FontStyle.Bold);

        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            var r = face.Region;
            Color color = face.Distance is null ? DetectOnlyColor : face.IsKnown ? KnownColor : UnknownColor;

            using (var pen = new Pen(color, 3 * scale))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            string text = $"{i + 1}. {face.Caption}";
            var size = g.MeasureString(text, font);
            float y = r.Y - size.Height - 2;
            if (y < 0) y = r.Y + r.Height + 2; // Üstte yer yoksa altına yaz

            using (var bg = new SolidBrush(Color.FromArgb(210, color)))
                g.FillRectangle(bg, r.X - 1.5f * scale, y, size.Width + 4, size.Height);
            g.DrawString(text, font, Brushes.White, r.X + 1, y);
        }
    }
}
