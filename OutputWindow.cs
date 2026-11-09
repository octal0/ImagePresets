using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImagePresets;

/// <summary>Ventana de proyección: la imagen a pantalla completa en el monitor de salida, sin interfaz.</summary>
public sealed class OutputWindow : Window
{
    private readonly MonitorInfo monitor;

    public ImageView ImageView { get; } = new() { IsEditor = false };
    public ViewAnimator Animator { get; }

    /// <summary>Teclas pulsadas con la proyección activa (p. ej. un mando de presentaciones).</summary>
    public event Action<KeyEventArgs>? KeyPressed;

    public OutputWindow(MonitorInfo monitor, BitmapSource image, View view)
    {
        this.monitor = monitor;
        Title = "Proyección";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 640;
        Height = 360;

        ImageView.Image = image;
        ImageView.View = view;
        Content = ImageView;
        Animator = new ViewAnimator(ImageView);

        SourceInitialized += (_, _) => Monitors.MoveWindow(new WindowInteropHelper(this).Handle, monitor);
        // Maximizar sin bordes cubre todo el monitor donde está la ventana, también la barra de tareas
        Loaded += (_, _) => WindowState = WindowState.Maximized;
        Closed += (_, _) => Animator.Stop();
    }

    public MonitorInfo Monitor => monitor;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        KeyPressed?.Invoke(e);
    }
}
