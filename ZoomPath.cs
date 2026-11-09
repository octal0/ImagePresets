namespace ImagePresets;

/// <summary>
/// Camino óptimo de zoom y desplazamiento entre dos encuadres (van Wijk y Nuij, 2003,
/// "Smooth and efficient zooming and panning"). Se aleja lo justo para viajar y vuelve a
/// acercarse, con velocidad percibida constante a lo largo del parámetro s ∈ [0, Length].
/// </summary>
public sealed class ZoomPath
{
    private readonly View from;
    private readonly View to;
    private readonly double dirX, dirY;   // dirección unitaria del viaje
    private readonly double rho;          // > 1: se aleja más; < 1: viaja más cerca
    private readonly double r0;
    private readonly bool pureZoom;       // mismo centro: solo zoom

    /// <summary>Longitud del camino (adimensional). 0 = mismo encuadre.</summary>
    public double Length { get; }

    public View End => to;

    public ZoomPath(View from, View to, double rho)
    {
        this.from = from;
        this.to = to;
        this.rho = Math.Max(0.05, rho);

        double w0 = from.W, w1 = to.W;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double u1 = Math.Sqrt(dx * dx + dy * dy);

        if (u1 < 1e-6 * Math.Max(w0, w1))
        {
            pureZoom = true;
            Length = Math.Abs(Math.Log(w1 / w0)) / this.rho;
            return;
        }

        dirX = dx / u1;
        dirY = dy / u1;
        double rho2 = this.rho * this.rho, rho4 = rho2 * rho2;
        double b0 = (w1 * w1 - w0 * w0 + rho4 * u1 * u1) / (2 * w0 * rho2 * u1);
        double b1 = (w1 * w1 - w0 * w0 - rho4 * u1 * u1) / (2 * w1 * rho2 * u1);
        // ln(-b + sqrt(b² + 1)) = -asinh(b), sin pérdida de precisión con b grande
        r0 = -Math.Asinh(b0);
        double r1 = -Math.Asinh(b1);
        Length = (r1 - r0) / this.rho;
    }

    /// <summary>Encuadre en la fracción t ∈ [0, 1] del camino.</summary>
    public View At(double t)
    {
        if (t <= 0) return from;
        if (t >= 1) return to;

        if (pureZoom)
        {
            return new View(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.W * Math.Pow(to.W / from.W, t));
        }

        double s = t * Length;
        double w0 = from.W;
        double coshR0 = Math.Cosh(r0);
        double u = w0 / (rho * rho) * (coshR0 * Math.Tanh(rho * s + r0) - Math.Sinh(r0));
        double w = w0 * coshR0 / Math.Cosh(rho * s + r0);
        return new View(from.X + dirX * u, from.Y + dirY * u, w);
    }
}
