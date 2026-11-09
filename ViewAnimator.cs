using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ImagePresets;

/// <summary>
/// Lleva un ImageView de un encuadre a otro: con un viaje de zoom (ZoomPath) si es la misma imagen,
/// o con un fundido a negro, cambio de imagen y fundido de vuelta si es otra.
/// </summary>
public sealed class ViewAnimator
{
    private static readonly IEasingFunction FadeEase = Frozen(new SineEase { EasingMode = EasingMode.EaseInOut });

    private readonly ImageView target;
    private readonly Stopwatch clock = new();
    private ZoomPath? path;
    private double duration;
    private bool hooked;
    private bool fadingOut;   // fundiendo a negro, antes de cambiar la imagen
    private int fadeToken;    // descarta los fundidos que se han interrumpido

    public ViewAnimator(ImageView target) => this.target = target;

    public bool IsRunning => path != null;

    /// <summary>Va al encuadre: viaje si es la misma imagen, fundido si es otra.</summary>
    public void GoTo(BitmapSource image, View view, double maxDuration, double rho, double fade)
    {
        if (fadingOut || target.Image != image)
            FadeTo(image, view, fade);
        else
            Start(view, maxDuration, rho);
    }

    /// <summary>Muestra la imagen y el encuadre al instante, sin transición.</summary>
    public void Show(BitmapSource image, View view)
    {
        Stop();
        fadeToken++;
        fadingOut = false;
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 1;
        target.Image = image;
        target.View = view;
    }

    /// <summary>Funde a negro, cambia de imagen y encuadre y vuelve a aparecer.</summary>
    public void FadeTo(BitmapSource image, View view, double fade)
    {
        if (fade <= 0)
        {
            Show(image, view);
            return;
        }

        Stop();
        int token = ++fadeToken;
        fadingOut = true;

        // Si ya estaba medio fundido, solo le queda la parte que falta
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(Math.Max(0.01, fade * target.Opacity))) { EasingFunction = FadeEase };
        fadeOut.Completed += (_, _) =>
        {
            if (token != fadeToken) return;
            fadingOut = false;
            target.Image = image;
            target.View = view;
            target.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromSeconds(fade)) { EasingFunction = FadeEase });
        };
        target.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <param name="maxDuration">Duración de los viajes largos; los cortos tardan menos.</param>
    /// <param name="rho">Cuánto se aleja durante el viaje (1,4 ≈ el valor clásico).</param>
    public void Start(View to, double maxDuration, double rho)
    {
        var p = new ZoomPath(target.View, to, rho);
        if (p.Length < 1e-3 || maxDuration <= 0)
        {
            Stop();
            target.View = to;
            return;
        }

        path = p;
        // Un retoque pequeño no debe tardar lo mismo que cruzar la imagen entera
        duration = maxDuration * Math.Clamp(p.Length / 2.0, 0.35, 1.0);
        clock.Restart();
        if (!hooked)
        {
            CompositionTarget.Rendering += OnFrame;
            hooked = true;
        }
    }

    /// <summary>Detiene el viaje de zoom (los fundidos siguen).</summary>
    public void Stop()
    {
        path = null;
        if (hooked)
        {
            CompositionTarget.Rendering -= OnFrame;
            hooked = false;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (path == null) return;
        double t = clock.Elapsed.TotalSeconds / duration;
        if (t >= 1)
        {
            target.View = path.End;
            Stop();
            return;
        }
        target.View = path.At(Ease(t));
    }

    /// <summary>Arranque y frenada suaves (smootherstep).</summary>
    private static double Ease(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
