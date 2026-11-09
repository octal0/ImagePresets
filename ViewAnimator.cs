using System.Diagnostics;
using System.Windows.Media;

namespace ImagePresets;

/// <summary>Anima el encuadre de un ImageView siguiendo un ZoomPath, fotograma a fotograma.</summary>
public sealed class ViewAnimator
{
    private readonly ImageView target;
    private readonly Stopwatch clock = new();
    private ZoomPath? path;
    private double duration;
    private bool hooked;

    public ViewAnimator(ImageView target) => this.target = target;

    public bool IsRunning => path != null;

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
}
