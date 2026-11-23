using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using AccessoryBatteryTray.Features.Bluetooth;
using AccessoryBatteryTray.Features.Bluetooth.Common;

namespace AccessoryBatteryTray.Features.Tray;

internal sealed class DeviceTile(bool monochromeFallback) : Control
{
    private AccessoryKind _kind;
    private byte _percent;
    private Bitmap? _systemGlyph;
    private bool _tintSystemGlyph;

    public void UpdateDevice(AccessoryKind kind, byte percent, string description)
    {
        _kind = kind;
        _percent = percent;
        AccessibleName = description;
        Invalidate();
    }

    public void SetGlyph(Bitmap glyph)
    {
        _systemGlyph?.Dispose();
        _systemGlyph = glyph;
        _tintSystemGlyph = glyph.GetPixel(0, 0).A == 0 &&
                           glyph.GetPixel(glyph.Width - 1, glyph.Height - 1).A == 0;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _systemGlyph?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var glyphSize = 24 * DeviceDpi / 96;
        var gap = 7 * DeviceDpi / 96;
        var percentage = $"{_percent}%";
        using var font = new Font(Font, FontStyle.Bold);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var textSize = TextRenderer.MeasureText(e.Graphics, percentage, font, Size.Empty, flags);
        var contentWidth = glyphSize + gap + textSize.Width;
        var left = Math.Max(0, (Width - contentWidth) / 2);

        var bounds = new Rectangle(left, (Height - glyphSize) / 2, glyphSize, glyphSize);
        if (_systemGlyph is { } systemGlyph)
            DrawSystemGlyph(e.Graphics, systemGlyph, bounds);
        else
            DrawGlyph(e.Graphics, _kind, bounds);
        var textPosition = new Point(left + glyphSize + gap, (Height - textSize.Height) / 2);
        TextRenderer.DrawText(e.Graphics, percentage, font, textPosition, ForeColor, flags);
    }

    private void DrawSystemGlyph(Graphics graphics, Bitmap glyph, Rectangle bounds)
    {
        var scale = Math.Min((float)bounds.Width / glyph.Width, (float)bounds.Height / glyph.Height);
        var width = Math.Max(1, (int)Math.Round(glyph.Width * scale));
        var height = Math.Max(1, (int)Math.Round(glyph.Height * scale));
        var destination = new Rectangle(
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2, width, height);
        if (!_tintSystemGlyph)
        {
            graphics.DrawImage(glyph, destination);
            return;
        }

        // Preserve the alpha channel and use the panel foreground for light and dark glyphs alike.
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
            Matrix40 = ForeColor.R / 255f, Matrix41 = ForeColor.G / 255f, Matrix42 = ForeColor.B / 255f
        });
        graphics.DrawImage(glyph, destination, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, attributes);
    }

    private void DrawGlyph(Graphics graphics, AccessoryKind device, Rectangle bounds)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);

        var accent = device switch
        {
            AccessoryKind.Keyboard => Color.FromArgb(161, 190, 255),
            AccessoryKind.Mouse => Color.FromArgb(133, 216, 188),
            AccessoryKind.Headphones => Color.FromArgb(231, 176, 244),
            _ => Color.FromArgb(186, 200, 219)
        };
        using var fill = new SolidBrush(monochromeFallback || SystemInformation.HighContrast ? ForeColor : accent);
        using var detail = new SolidBrush(BackColor);

        switch (device)
        {
            case AccessoryKind.Keyboard:
                DrawKeyboard(graphics, fill, detail);
                break;
            case AccessoryKind.Mouse:
                if (monochromeFallback)
                {
                    using var outline = new Pen(fill, 1.5f);
                    graphics.DrawEllipse(outline, 5.75f, 1.75f, 12.5f, 20.5f);
                    graphics.DrawLine(outline, 12, 5.5f, 12, 8.5f);
                    break;
                }
                graphics.FillEllipse(fill, 5, 1, 14, 22);
                graphics.FillRectangle(detail, 6, 9, 12, 1);
                graphics.FillEllipse(detail, 10.6f, 3.8f, 2.8f, 4.2f);
                break;
            case AccessoryKind.Headphones:
                using (var headband = new Pen(fill, 2.4f))
                    graphics.DrawArc(headband, 3.5f, 2.5f, 17, 17, 180, 180);
                graphics.FillEllipse(fill, 2.5f, 10.5f, 6.5f, 10);
                graphics.FillEllipse(fill, 15, 10.5f, 6.5f, 10);
                break;
            default:
                using (var outline = new Pen(fill, 1.8f))
                    graphics.DrawLines(outline,
                        new Point[] { new(6, 7), new(17, 16), new(12, 21), new(12, 3), new(17, 8), new(6, 17) });
                break;
        }

        graphics.Restore(state);
    }

    private static void DrawKeyboard(Graphics graphics, Brush fill, Brush detail)
    {
        graphics.FillRoundedRectangle(fill, new RectangleF(1, 5, 22, 14), new SizeF(3, 3));
        for (var x = 4; x <= 19; x += 3)
        {
            graphics.FillRectangle(detail, x, 8, 1.6f, 1.6f);
            graphics.FillRectangle(detail, x, 11, 1.6f, 1.6f);
        }
        graphics.FillRectangle(detail, 7, 15, 10, 1.4f);
    }
}
