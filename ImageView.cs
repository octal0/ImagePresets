using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImagePresets;

/// <summary>
/// Dibuja la imagen con un encuadre (View). En modo editor muestra el marco con la proporción
/// del monitor de salida, oscurece lo que queda fuera y permite mover (arrastrar) y hacer zoom (rueda).
/// </summary>
public class ImageView : FrameworkElement
{
    private const double FrameMargin = 24;

    private static readonly Brush EditorBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18)));
    private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));
    private static readonly Pen FramePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)), 1.5));

    private BitmapSource? image;
    private View view = new(0, 0, 1);
    private double aspect = 16.0 / 9.0;
    private Point? dragStart;
    private View dragView;

    /// <summary>El usuario ha movido o ampliado la vista (para cancelar animaciones).</summary>
    public event Action? UserChangedView;

    public bool IsEditor { get; set; }

    public BitmapSource? Image
    {
        get => image;
        set { image = value; InvalidateVisual(); }
    }

    public View View
    {
        get => view;
        set { view = value; InvalidateVisual(); }
    }

    /// <summary>Proporción ancho/alto del monitor de salida (solo afecta al marco del editor).</summary>
    public double Aspect
    {
        get => aspect;
        set { aspect = value; InvalidateVisual(); }
    }

    public ImageView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        ClipToBounds = true;
        SnapsToDevicePixels = false;
    }

    /// <summary>Rectángulo de pantalla que corresponde al encuadre.</summary>
    public Rect FrameRect
    {
        get
        {
            double w = ActualWidth, h = ActualHeight;
            if (!IsEditor) return new Rect(0, 0, w, h);
            double aw = Math.Max(1, w - 2 * FrameMargin), ah = Math.Max(1, h - 2 * FrameMargin);
            double fw = Math.Min(aw, ah * aspect), fh = fw / aspect;
            return new Rect((w - fw) / 2, (h - fh) / 2, fw, fh);
        }
    }

    private double Scale => view.W > 0 ? FrameRect.Width / view.W : 1;

    public Point ImageToScreen(Point p)
    {
        var f = FrameRect;
        double s = Scale;
        return new Point(f.X + f.Width / 2 + (p.X - view.X) * s, f.Y + f.Height / 2 + (p.Y - view.Y) * s);
    }

    public Point ScreenToImage(Point p)
    {
        var f = FrameRect;
        double s = Scale;
        return new Point(view.X + (p.X - f.X - f.Width / 2) / s, view.Y + (p.Y - f.Y - f.Height / 2) / s);
    }

    /// <summary>Encuadre que muestra la imagen entera dentro del marco.</summary>
    public static View Fit(BitmapSource img, double aspect) =>
        new(img.PixelWidth / 2.0, img.PixelHeight / 2.0, Math.Max(img.PixelWidth, img.PixelHeight * aspect));

    protected override void OnRender(DrawingContext dc)
    {
        var all = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(IsEditor ? EditorBackground : Brushes.Black, null, all);

        if (image == null)
        {
            if (IsEditor)
            {
                var text = new FormattedText("Abre una imagen para empezar (botón «Abrir imagen…»)",
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18,
                    Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
            }
            return;
        }

        var topLeft = ImageToScreen(new Point(0, 0));
        double s = Scale;
        dc.DrawImage(image, new Rect(topLeft.X, topLeft.Y, image.PixelWidth * s, image.PixelHeight * s));

        if (IsEditor)
        {
            var f = FrameRect;
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(f));
            dc.DrawGeometry(Dim, null, outside);
            dc.DrawRectangle(null, FramePen, f);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEditor || image == null) return;
        UserChangedView?.Invoke();

        var mouse = e.GetPosition(this);
        var anchor = ScreenToImage(mouse);
        double factor = Math.Pow(1.0015, e.Delta);   // una muesca (120) ≈ ×1,2
        double newW = Math.Clamp(view.W / factor, image.PixelWidth / 400.0, image.PixelWidth * 10.0);
        var f = FrameRect;
        double ns = f.Width / newW;
        // El punto bajo el ratón se queda quieto
        View = new View(anchor.X - (mouse.X - f.X - f.Width / 2) / ns,
                        anchor.Y - (mouse.Y - f.Y - f.Height / 2) / ns, newW);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsEditor || image == null) return;
        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            UserChangedView?.Invoke();
            dragStart = e.GetPosition(this);
            dragView = view;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStart is not Point start) return;
        var p = e.GetPosition(this);
        double s = FrameRect.Width / dragView.W;
        View = dragView with { X = dragView.X - (p.X - start.X) / s, Y = dragView.Y - (p.Y - start.Y) / s };
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragStart == null) return;
        dragStart = null;
        ReleaseMouseCapture();
        Cursor = null;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        dragStart = null;
        Cursor = null;
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
