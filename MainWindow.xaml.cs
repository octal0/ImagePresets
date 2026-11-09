using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ImagePresets;

public partial class MainWindow : Window
{
    private const string ImageFilter = "Imágenes|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.webp|Todos los archivos|*.*";
    private const string PresetFilter = "Presets de imagen|*.presets.json;*.json";

    private readonly ObservableCollection<Preset> presets = new();
    private readonly Dictionary<string, BitmapSource> imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ViewAnimator editorAnimator;
    private readonly AppSettings settings = AppSettings.Load();
    private readonly List<MonitorInfo> monitors;

    private string? editorImagePath;   // imagen que se ve (o se va a ver) en el editor
    private string? presentationPath;
    private OutputWindow? output;
    private int current = -1;
    private bool dirty;
    private bool loading;

    public MainWindow()
    {
        InitializeComponent();

        PresetList.ItemsSource = presets;
        presets.CollectionChanged += Presets_CollectionChanged;
        editorAnimator = new ViewAnimator(Editor);
        Editor.UserChangedView += editorAnimator.Stop;

        monitors = Monitors.GetAll();
        MonitorBox.ItemsSource = monitors;
        MonitorBox.SelectedItem =
            monitors.FirstOrDefault(m => m.Device == settings.OutputDevice)
            ?? monitors.FirstOrDefault(m => !m.Primary)
            ?? monitors.FirstOrDefault();
        Editor.Aspect = Aspect;

        // Se enganchan aquí y no en el XAML: así no saltan mientras se crea la ventana
        MonitorBox.SelectionChanged += MonitorBox_SelectionChanged;
        DurationSlider.ValueChanged += (_, _) => { UpdateSliderTexts(); MarkDirty(); };
        RhoSlider.ValueChanged += (_, _) => { UpdateSliderTexts(); MarkDirty(); };
        FadeSlider.ValueChanged += (_, _) => { UpdateSliderTexts(); MarkDirty(); };
        UpdateSliderTexts();

        PreviewKeyDown += Window_PreviewKeyDown;
        Closing += Window_Closing;
        Loaded += (_, _) => OpenStartupPresentation();
        UpdateTitle();
    }

    private double Aspect => MonitorBox.SelectedItem is MonitorInfo m ? m.Aspect : 16.0 / 9.0;

    // ───────────── Archivos ─────────────

    /// <summary>Abre el archivo pasado por la línea de órdenes o, si no hay, el último usado.</summary>
    private void OpenStartupPresentation()
    {
        var args = Environment.GetCommandLineArgs();
        string? path = args.Length > 1 ? args[1] : settings.LastPresentation;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        TryLoadPresentation(path, "No se pudo abrir el archivo de presets.");
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Abrir imagen", Filter = ImageFilter };
        if (dialog.ShowDialog(this) != true) return;

        // Si la imagen ya tiene presets guardados a su lado, se abren con ella
        string sibling = PresentationData.DefaultPathFor(dialog.FileName);
        if (File.Exists(sibling))
        {
            TryLoadPresentation(sibling, "No se pudo abrir el archivo de presets.");
            return;
        }

        try
        {
            imageCache.Clear();
            var preset = NewFullPreset(dialog.FileName);
            loading = true;
            presets.Clear();
            presets.Add(preset);
            ShowInEditor(preset);
            output?.Animator.Show(preset.Image!, preset.View);
            presentationPath = sibling;
            current = -1;
            loading = false;
            MarkDirty();
        }
        catch (Exception ex)
        {
            loading = false;
            ShowError("No se pudo abrir la imagen.", ex);
        }
    }

    /// <summary>Añade otra imagen a la presentación, con un preset que la muestra entera.</summary>
    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Image == null)
        {
            OpenImage_Click(sender, e);
            return;
        }

        var dialog = new OpenFileDialog { Title = "Añadir imagen", Filter = ImageFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            int index = current >= 0 ? current + 1 : presets.Count;
            int first = index;
            foreach (var file in dialog.FileNames)
                presets.Insert(index++, NewFullPreset(file));
            MarkDirty();
            GoTo(first);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir la imagen.", ex);
        }
    }

    private void OpenPresets_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Abrir presets", Filter = PresetFilter };
        if (dialog.ShowDialog(this) != true) return;
        TryLoadPresentation(dialog.FileName, "No se pudo abrir el archivo de presets.");
    }

    private void TryLoadPresentation(string path, string errorMessage)
    {
        try
        {
            LoadPresentation(path);
        }
        catch (OperationCanceledException)
        {
            // El usuario no ha querido buscar una imagen que falta
        }
        catch (Exception ex)
        {
            ShowError(errorMessage, ex);
        }
    }

    private void LoadPresentation(string jsonPath)
    {
        var data = PresentationData.Load(jsonPath);
        var relocatedImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Ruta de cada imagen; si falta, se pide al usuario (una vez por imagen)
        string Resolve(string? image)
        {
            string path = PresentationData.ResolvePath(jsonPath, string.IsNullOrEmpty(image) ? data.Image : image);
            if (relocatedImages.TryGetValue(path, out var moved)) return moved;
            if (File.Exists(path)) return path;

            MessageBox.Show(this, $"No se encuentra la imagen:\n{path}\n\nElige dónde está.", "Imagen no encontrada",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            var dialog = new OpenFileDialog { Title = "Buscar la imagen", Filter = ImageFilter };
            if (dialog.ShowDialog(this) != true) throw new OperationCanceledException();
            relocatedImages[path] = dialog.FileName;
            return dialog.FileName;
        }

        imageCache.Clear();
        var loaded = new List<Preset>();
        foreach (var p in data.Presets)
        {
            string path = Resolve(p.Image);
            var bmp = GetImage(path);
            loaded.Add(new Preset
            {
                Name = p.Name,
                ImagePath = path,
                Image = bmp,
                View = new View(p.X * bmp.PixelWidth, p.Y * bmp.PixelHeight, p.Width * bmp.PixelWidth),
            });
        }
        // Un archivo sin presets todavía tiene su imagen general
        string? emptyImage = loaded.Count == 0 && !string.IsNullOrEmpty(data.Image) ? Resolve(null) : null;

        loading = true;
        try
        {
            presets.Clear();
            foreach (var p in loaded) presets.Add(p);
            DurationSlider.Value = data.Duration;
            RhoSlider.Value = data.Rho;
            FadeSlider.Value = data.Fade;
            RefreshThumbnails();

            if (loaded.Count > 0)
            {
                ShowInEditor(loaded[0]);
            }
            else if (emptyImage != null)
            {
                var bmp = GetImage(emptyImage);
                editorAnimator.Show(bmp, ImageView.Fit(bmp, Aspect));
                editorImagePath = emptyImage;
            }
            if (Editor.Image != null) output?.Animator.Show(Editor.Image, Editor.View);

            presentationPath = jsonPath;
            current = -1;
            dirty = relocatedImages.Count > 0;
        }
        finally
        {
            loading = false;
        }

        settings.LastPresentation = jsonPath;
        settings.Save();
        UpdateTitle();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    private bool Save() => presentationPath == null ? SaveAs() : SaveTo(presentationPath);

    private bool SaveAs()
    {
        string? baseImage = presets.Count > 0 ? presets[0].ImagePath : editorImagePath;
        if (baseImage == null) return false;
        var dialog = new SaveFileDialog
        {
            Title = "Guardar presets",
            Filter = PresetFilter,
            FileName = Path.GetFileName(presentationPath ?? PresentationData.DefaultPathFor(baseImage)),
            InitialDirectory = Path.GetDirectoryName(presentationPath ?? baseImage),
        };
        return dialog.ShowDialog(this) == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        string? baseImage = presets.Count > 0 ? presets[0].ImagePath : editorImagePath;
        if (baseImage == null) return false;

        var data = new PresentationData
        {
            Image = PresentationData.RelativeImagePath(path, baseImage),
            Duration = Math.Round(DurationSlider.Value, 2),
            Rho = Math.Round(RhoSlider.Value, 2),
            Fade = Math.Round(FadeSlider.Value, 2),
            Presets = presets.Select(p => new PresetData
            {
                Name = p.Name,
                Image = PresentationData.RelativeImagePath(path, p.ImagePath),
                X = Math.Round(p.View.X / p.Image!.PixelWidth, 6),
                Y = Math.Round(p.View.Y / p.Image.PixelHeight, 6),
                Width = Math.Round(p.View.W / p.Image.PixelWidth, 6),
            }).ToList(),
        };

        try
        {
            data.Save(path);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar.", ex);
            return false;
        }

        presentationPath = path;
        dirty = false;
        settings.LastPresentation = path;
        settings.Save();
        UpdateTitle();
        return true;
    }

    /// <summary>Carga una imagen (o la devuelve si ya estaba cargada).</summary>
    private BitmapSource GetImage(string path)
    {
        if (imageCache.TryGetValue(path, out var cached)) return cached;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // no deja el archivo bloqueado
        bmp.EndInit();
        bmp.Freeze();
        imageCache[path] = bmp;
        return bmp;
    }

    // ───────────── Presets ─────────────

    /// <summary>Preset nuevo que muestra la imagen entera, con el nombre del archivo.</summary>
    private Preset NewFullPreset(string imagePath)
    {
        var bmp = GetImage(imagePath);
        var preset = new Preset
        {
            Name = Path.GetFileNameWithoutExtension(imagePath),
            ImagePath = imagePath,
            Image = bmp,
            View = ImageView.Fit(bmp, Aspect),
        };
        preset.Thumbnail = MakeThumbnail(preset);
        return preset;
    }

    private void ShowInEditor(Preset preset)
    {
        editorAnimator.Show(preset.Image!, preset.View);
        editorImagePath = preset.ImagePath;
        UpdateTitle();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Image == null || editorImagePath == null) return;
        var preset = new Preset
        {
            Name = $"Preset {presets.Count + 1}",
            ImagePath = editorImagePath,
            Image = Editor.Image,
            View = Editor.View,
        };
        preset.Thumbnail = MakeThumbnail(preset);
        int index = current >= 0 ? current + 1 : presets.Count;
        presets.Insert(index, preset);
        Select(index);
        MarkDirty();
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset || Editor.Image == null || editorImagePath == null) return;
        preset.ImagePath = editorImagePath;
        preset.Image = Editor.Image;
        preset.View = Editor.View;
        preset.Thumbnail = MakeThumbnail(preset);
        MarkDirty();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        int index = PresetList.SelectedIndex;
        if (index < 0) return;
        presets.RemoveAt(index);
        Select(Math.Min(index, presets.Count - 1));
        MarkDirty();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        int index = PresetList.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= presets.Count) return;
        presets.Move(index, target);
        Select(target);
        MarkDirty();
    }

    private void Select(int index)
    {
        current = index;
        PresetList.SelectedIndex = index;
        if (index >= 0) PresetList.ScrollIntoView(presets[index]);
    }

    /// <summary>
    /// Va al preset en el editor y, si se está proyectando, también en la salida: con un viaje de zoom
    /// si es la misma imagen o con un fundido a negro si es otra.
    /// </summary>
    private void GoTo(int index)
    {
        if (index < 0 || index >= presets.Count) return;
        Select(index);
        var p = presets[index];
        editorAnimator.GoTo(p.Image!, p.View, DurationSlider.Value, RhoSlider.Value, FadeSlider.Value);
        output?.Animator.GoTo(p.Image!, p.View, DurationSlider.Value, RhoSlider.Value, FadeSlider.Value);
        editorImagePath = p.ImagePath;
        UpdateTitle();
    }

    private void PresetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || IsInside<TextBox>(source)) return;
        if (ItemsControl.ContainerFromElement(PresetList, source) is ListBoxItem item)
            GoTo(PresetList.ItemContainerGenerator.IndexFromContainer(item));
    }

    private void Presets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Preset p in e.NewItems)
                p.PropertyChanged += Preset_PropertyChanged;
        if (e.OldItems != null)
            foreach (Preset p in e.OldItems)
                p.PropertyChanged -= Preset_PropertyChanged;
        for (int i = 0; i < presets.Count; i++)
            presets[i].Number = i + 1;
        UpdateTitle();
    }

    private void Preset_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preset.Name)) MarkDirty();
    }

    private void RefreshThumbnails()
    {
        foreach (var p in presets)
            p.Thumbnail = MakeThumbnail(p);
    }

    /// <summary>Miniatura del encuadre (con negro donde se sale de la imagen).</summary>
    private ImageSource? MakeThumbnail(Preset preset)
    {
        var image = preset.Image;
        if (image == null) return null;
        var v = preset.View;
        double h = v.W / Aspect;
        var frame = new Rect(v.X - v.W / 2, v.Y - h / 2, v.W, h);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(frame) };
        group.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(frame)));
        group.Children.Add(new ImageDrawing(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
        var thumb = new DrawingImage(group);
        thumb.Freeze();
        return thumb;
    }

    // ───────────── Proyección ─────────────

    private void Project_Click(object sender, RoutedEventArgs e) => ToggleProjection();

    private void ToggleProjection()
    {
        if (output != null)
        {
            output.Close();
            return;
        }
        if (MonitorBox.SelectedItem is not MonitorInfo monitor) return;

        BitmapSource? image;
        View view;
        if (current >= 0 && current < presets.Count)
        {
            image = presets[current].Image;
            view = presets[current].View;
        }
        else
        {
            image = Editor.Image;
            view = Editor.View;
        }
        if (image == null) return;

        output = new OutputWindow(monitor, image, view);
        output.KeyPressed += e => { if (HandleKey(e.Key)) e.Handled = true; };
        output.Closed += (_, _) =>
        {
            output = null;
            ProjectButton.Content = "▶ Proyectar (F5)";
            Activate();
        };
        output.Show();
        ProjectButton.Content = "■ Detener (F5)";
        // El teclado sigue en esta ventana (salvo que la salida sea el mismo monitor)
        if (!IsOnMonitor(monitor)) Activate();
    }

    private bool IsOnMonitor(MonitorInfo monitor)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return false;
        var center = source.CompositionTarget.TransformToDevice.Transform(new Point(Left + Width / 2, Top + Height / 2));
        return center.X >= monitor.X && center.X < monitor.X + monitor.Width
            && center.Y >= monitor.Y && center.Y < monitor.Y + monitor.Height;
    }

    private void MonitorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorBox.SelectedItem is not MonitorInfo monitor) return;
        settings.OutputDevice = monitor.Device;
        settings.Save();
        Editor.Aspect = Aspect;
        RefreshThumbnails();
        if (output != null)
        {
            output.Close();
            ToggleProjection();
        }
    }

    // ───────────── Teclado ─────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            // Escribiendo un nombre: Intro o Esc terminan la edición
            if (e.Key is Key.Enter or Key.Escape)
            {
                PresetList.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                ToggleProjection();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save();
            e.Handled = true;
            return;
        }

        if (HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>Teclas de navegación, compartidas por esta ventana y la de proyección.</summary>
    private bool HandleKey(Key key)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return false;
        switch (key)
        {
            case Key.Right:
            case Key.Down:
            case Key.PageDown:
            case Key.Space:
                GoTo(Math.Min(current + 1, presets.Count - 1));
                return true;
            case Key.Left:
            case Key.Up:
            case Key.PageUp:
                GoTo(Math.Max(current - 1, 0));
                return true;
            case Key.Home:
                GoTo(0);
                return true;
            case Key.End:
                GoTo(presets.Count - 1);
                return true;
            case Key.F5:
                ToggleProjection();
                return true;
            case >= Key.D1 and <= Key.D9:
                GoTo(key - Key.D1);
                return true;
            case >= Key.NumPad1 and <= Key.NumPad9:
                GoTo(key - Key.NumPad1);
                return true;
            default:
                return false;
        }
    }

    // ───────────── Varios ─────────────

    private void MarkDirty()
    {
        if (loading || Editor.Image == null) return;
        dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = presentationPath != null ? Path.GetFileName(presentationPath) : "Sin archivo";
        Title = $"{name}{(dirty ? " *" : "")} — Presets de imagen";

        if (editorImagePath == null || !imageCache.TryGetValue(editorImagePath, out var image))
        {
            FileText.Text = "";
            return;
        }
        int imageCount = presets.Select(p => p.ImagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        FileText.Text = $"{Path.GetFileName(editorImagePath)} · {image.PixelWidth}×{image.PixelHeight}"
            + (imageCount > 1 ? $" · {imageCount} imágenes" : "");
    }

    private void UpdateSliderTexts()
    {
        DurationText.Text = $"{DurationSlider.Value:0.0} s";
        RhoText.Text = $"{RhoSlider.Value:0.0}";
        FadeText.Text = FadeSlider.Value < 0.05 ? "corte" : $"{FadeSlider.Value:0.0} s";
    }

    /// <summary>Si hay cambios sin guardar, pregunta. Devuelve false si el usuario cancela.</summary>
    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "Hay cambios sin guardar. ¿Guardarlos?", "Presets de imagen",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        output?.Close();
        editorAnimator.Stop();
    }

    private void ShowError(string message, Exception ex) =>
        MessageBox.Show(this, $"{message}\n\n{ex.Message}", "Presets de imagen", MessageBoxButton.OK, MessageBoxImage.Error);

    private static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
