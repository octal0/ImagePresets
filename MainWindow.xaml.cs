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
    private readonly ViewAnimator editorAnimator;
    private readonly AppSettings settings = AppSettings.Load();
    private readonly List<MonitorInfo> monitors;

    private BitmapSource? image;
    private string? imagePath;
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
        UpdateSliderTexts();

        PreviewKeyDown += Window_PreviewKeyDown;
        Closing += Window_Closing;
        Loaded += (_, _) => OpenLastPresentation();
        UpdateTitle();
    }

    private double Aspect => MonitorBox.SelectedItem is MonitorInfo m ? m.Aspect : 16.0 / 9.0;

    // ───────────── Archivos ─────────────

    private void OpenLastPresentation()
    {
        var last = settings.LastPresentation;
        if (string.IsNullOrEmpty(last) || !File.Exists(last)) return;
        try
        {
            LoadPresentation(last);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir el último archivo de presets.", ex);
        }
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Abrir imagen", Filter = ImageFilter };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // Si la imagen ya tiene presets guardados a su lado, se abren con ella
            string sibling = PresentationData.DefaultPathFor(dialog.FileName);
            if (File.Exists(sibling))
            {
                LoadPresentation(sibling);
                return;
            }

            var bmp = LoadBitmap(dialog.FileName);
            loading = true;
            SetImage(bmp, dialog.FileName);
            presets.Clear();
            presets.Add(new Preset { Name = "Vista completa", View = ImageView.Fit(bmp, Aspect) });
            RefreshThumbnails();
            Editor.View = presets[0].View;
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

    private void OpenPresets_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Abrir presets", Filter = PresetFilter };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LoadPresentation(dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir el archivo de presets.", ex);
        }
    }

    private void LoadPresentation(string jsonPath)
    {
        var data = PresentationData.Load(jsonPath);
        string img = data.ResolveImage(jsonPath);
        bool relocated = false;

        if (!File.Exists(img))
        {
            MessageBox.Show(this, $"No se encuentra la imagen:\n{img}\n\nElige dónde está.", "Imagen no encontrada",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            var dialog = new OpenFileDialog { Title = "Buscar la imagen", Filter = ImageFilter };
            if (dialog.ShowDialog(this) != true) return;
            img = dialog.FileName;
            relocated = true;
        }

        var bmp = LoadBitmap(img);
        loading = true;
        try
        {
            SetImage(bmp, img);
            presets.Clear();
            foreach (var p in data.Presets)
            {
                presets.Add(new Preset
                {
                    Name = p.Name,
                    View = new View(p.X * bmp.PixelWidth, p.Y * bmp.PixelHeight, p.Width * bmp.PixelWidth),
                });
            }
            DurationSlider.Value = data.Duration;
            RhoSlider.Value = data.Rho;
            RefreshThumbnails();
            Editor.View = presets.Count > 0 ? presets[0].View : ImageView.Fit(bmp, Aspect);
            presentationPath = jsonPath;
            current = -1;
            dirty = relocated;
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
        if (image == null || imagePath == null) return false;
        var dialog = new SaveFileDialog
        {
            Title = "Guardar presets",
            Filter = PresetFilter,
            FileName = Path.GetFileName(presentationPath ?? PresentationData.DefaultPathFor(imagePath)),
            InitialDirectory = Path.GetDirectoryName(presentationPath ?? imagePath),
        };
        return dialog.ShowDialog(this) == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        if (image == null || imagePath == null) return false;
        double w = image.PixelWidth, h = image.PixelHeight;
        var data = new PresentationData
        {
            Image = PresentationData.RelativeImagePath(path, imagePath),
            Duration = Math.Round(DurationSlider.Value, 2),
            Rho = Math.Round(RhoSlider.Value, 2),
            Presets = presets.Select(p => new PresetData
            {
                Name = p.Name,
                X = Math.Round(p.View.X / w, 6),
                Y = Math.Round(p.View.Y / h, 6),
                Width = Math.Round(p.View.W / w, 6),
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

    private static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // no deja el archivo bloqueado
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void SetImage(BitmapSource bmp, string path)
    {
        image = bmp;
        imagePath = path;
        Editor.Image = bmp;
        editorAnimator.Stop();
        if (output != null)
        {
            output.Animator.Stop();
            output.ImageView.Image = bmp;
            output.ImageView.View = ImageView.Fit(bmp, output.Monitor.Aspect);
        }
    }

    // ───────────── Presets ─────────────

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (image == null) return;
        var preset = new Preset { Name = $"Preset {presets.Count + 1}", View = Editor.View };
        preset.Thumbnail = MakeThumbnail(preset.View);
        int index = current >= 0 ? current + 1 : presets.Count;
        presets.Insert(index, preset);
        Select(index);
        MarkDirty();
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset) return;
        preset.View = Editor.View;
        preset.Thumbnail = MakeThumbnail(preset.View);
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

    /// <summary>Viaja al preset en el editor y, si se está proyectando, también en la salida.</summary>
    private void GoTo(int index)
    {
        if (index < 0 || index >= presets.Count) return;
        Select(index);
        var view = presets[index].View;
        editorAnimator.Start(view, DurationSlider.Value, RhoSlider.Value);
        output?.Animator.Start(view, DurationSlider.Value, RhoSlider.Value);
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
    }

    private void Preset_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preset.Name)) MarkDirty();
    }

    private void RefreshThumbnails()
    {
        foreach (var p in presets)
            p.Thumbnail = MakeThumbnail(p.View);
    }

    /// <summary>Miniatura del encuadre (con negro donde se sale de la imagen).</summary>
    private ImageSource? MakeThumbnail(View v)
    {
        if (image == null) return null;
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
        if (image == null || MonitorBox.SelectedItem is not MonitorInfo monitor) return;

        var view = current >= 0 && current < presets.Count ? presets[current].View : Editor.View;
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
        if (loading || image == null) return;
        dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = presentationPath != null ? Path.GetFileName(presentationPath) : "Sin archivo";
        Title = $"{name}{(dirty ? " *" : "")} — Presets de imagen";
        FileText.Text = imagePath != null && image != null
            ? $"{Path.GetFileName(imagePath)} · {image.PixelWidth}×{image.PixelHeight}"
            : "";
    }

    private void UpdateSliderTexts()
    {
        DurationText.Text = $"{DurationSlider.Value:0.0} s";
        RhoText.Text = $"{RhoSlider.Value:0.0}";
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
