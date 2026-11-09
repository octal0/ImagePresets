using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImagePresets;

/// <summary>Un preset en la lista de la aplicación.</summary>
public sealed class Preset : INotifyPropertyChanged
{
    private string name = "";
    private int number;
    private ImageSource? thumbnail;

    /// <summary>Encuadre en píxeles de su imagen.</summary>
    public View View { get; set; }

    /// <summary>Ruta absoluta de la imagen de este preset.</summary>
    public string ImagePath { get; set; } = "";

    public BitmapSource? Image { get; set; }

    public string Name
    {
        get => name;
        set { if (name != value) { name = value; Changed(); } }
    }

    public int Number
    {
        get => number;
        set { if (number != value) { number = value; Changed(); } }
    }

    public ImageSource? Thumbnail
    {
        get => thumbnail;
        set { thumbnail = value; Changed(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>
/// Archivo de presets (JSON). Las posiciones se guardan normalizadas (0–1 respecto al ancho y alto
/// de la imagen), así sirven también si se cambia la imagen por otra versión con más resolución.
/// Cada preset puede tener su propia imagen; si no la indica, usa la imagen general («image»).
/// </summary>
public sealed class PresentationData
{
    public string Image { get; set; } = "";
    public double Duration { get; set; } = 3.0;
    public double Rho { get; set; } = 1.4;
    /// <summary>Segundos del fundido a negro (y de vuelta) al cambiar de imagen. 0 = corte.</summary>
    public double Fade { get; set; } = 0.6;
    public List<PresetData> Presets { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PresentationData Load(string path) =>
        JsonSerializer.Deserialize<PresentationData>(File.ReadAllText(path), Options) ?? new PresentationData();

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    /// <summary>Ruta absoluta de una imagen (en el archivo puede ir relativa a él).</summary>
    public static string ResolvePath(string jsonPath, string image) =>
        Path.IsPathRooted(image) ? image : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(jsonPath)!, image));

    public static string RelativeImagePath(string jsonPath, string imagePath)
    {
        string rel = Path.GetRelativePath(Path.GetDirectoryName(jsonPath)!, imagePath);
        return rel.StartsWith("..") || Path.IsPathRooted(rel) ? imagePath : rel;
    }

    /// <summary>Archivo de presets por defecto junto a la imagen: «imagen.presets.json».</summary>
    public static string DefaultPathFor(string imagePath) =>
        Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + ".presets.json");
}

public sealed class PresetData
{
    public string Name { get; set; } = "";
    public string? Image { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
}

/// <summary>Preferencias de la aplicación (último archivo y monitor de salida).</summary>
public sealed class AppSettings
{
    public string? LastPresentation { get; set; }
    public string? OutputDevice { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImagePresets", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Preferencias dañadas: se empieza de cero
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // No es grave si no se pueden guardar
        }
    }
}
