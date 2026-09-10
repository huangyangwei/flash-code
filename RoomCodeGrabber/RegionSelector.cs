using System.Runtime.Versioning;

namespace RoomCodeGrabber;

/// <summary>
/// 全屏半透明遮罩，拖拽框选抓取区域。Esc 取消，返回屏幕像素坐标（跨屏用虚拟屏幕坐标）。
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class RegionSelector : Form
{
    private Point _start;
    private Rectangle _current = Rectangle.Empty;
    private bool _dragging;

    public Rectangle? SelectedRegion { get; private set; }

    public RegionSelector()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        var vs = SystemInformation.VirtualScreen;
        Bounds = new Rectangle(vs.X, vs.Y, vs.Width, vs.Height);
        BackColor = Color.Black;
        Opacity = 0.35;
        ShowInTaskbar = false;
        TopMost = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _start = PointToScreen(e.Location);
            _current = Rectangle.Empty;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            var p = PointToScreen(e.Location);
            _current = RectFrom(_start, p);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _dragging)
        {
            _dragging = false;
            var r = RectFrom(_start, PointToScreen(e.Location));
            // 太小的框选视为误操作，忽略
            if (r.Width >= 8 && r.Height >= 8)
            {
                SelectedRegion = r;
                Close();
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_current.IsEmpty) return;
        using var pen = new Pen(Color.Lime, 2);
        var local = new Rectangle(_current.X - Left, _current.Y - Top, _current.Width, _current.Height);
        e.Graphics.DrawRectangle(pen, local);
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(60, Color.Lime)), local);
        e.Graphics.DrawString($"{_current.Width} x {_current.Height}",
            SystemFonts.DefaultFont, Brushes.Lime, local.X, local.Y - 24);
    }

    private static Rectangle RectFrom(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
