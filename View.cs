namespace ImagePresets;

/// <summary>
/// Un encuadre de la imagen: centro (X, Y) y ancho visible W, todo en píxeles de la imagen.
/// El alto visible depende de la proporción del monitor de salida (W / aspecto).
/// </summary>
public readonly record struct View(double X, double Y, double W);
