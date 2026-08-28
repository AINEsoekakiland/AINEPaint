using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AINEPaint.Brushes;
using AINEPaint.Color;
using AINEPaint.Drawing;
using AINEPaint.History;
using AINEPaint.IO;
using AINEPaint.Layers;
using AINEPaint.Selection;
using AINEPaint.Settings;
using AINEPaint.Views;
using Microsoft.Win32;
using SkiaSharp;

namespace AINEPaint;

public partial class MainWindow : Window
{
    private PaintDocument? _document;
    private readonly HistoryStack _history = new();

    /// <summary>レイヤー一覧の更新と選択変更が互いを呼び合わないようにするための番人。</summary>
    private bool _syncingLayers;

    /// <summary>保存先。まだ一度も保存していない場合は null。</summary>
    private string? _currentPath;

    /// <summary>最後に保存してから変更があるか。</summary>
    private bool _isDirty;

    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        Canvas.ViewStateChanged += UpdateStatus;
        Canvas.ColorPicked += ApplyBrushColor;
        Canvas.BeforeDocumentChange += OnBeforeDocumentChange;
        _history.Changed += UpdateHistoryMenu;

        _settings = SettingsStore.Load();
        ApplySettings(_settings);

        UpdateHistoryMenu();
        UpdateStatus();

        // ウィンドウが出来上がってから開く（エラー表示に親ウィンドウが要るため）
        Loaded += (_, _) => OpenFromCommandLine();
    }

    // ===== 設定 =====

    private void ApplySettings(AppSettings settings)
    {
        SizeSlider.Value = Math.Clamp(settings.BrushSize, SizeSlider.Minimum, SizeSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(settings.BrushOpacity * 100.0, 0, 100);
        ToleranceSlider.Value = Math.Clamp(settings.FillTolerance, ToleranceSlider.Minimum, ToleranceSlider.Maximum);
        FillExpandSlider.Value = Math.Clamp(settings.FillExpand, FillExpandSlider.Minimum, FillExpandSlider.Maximum);

        ApplyBrushColor(ColorUtil.TryParseHex(settings.BrushColor, out var color) ? color : SKColors.Black);

        _history.MaxEntries = Math.Max(1, settings.UndoMaxEntries);
        _history.MaxBytes = Math.Max(16, settings.UndoMaxMegabytes) * 1024L * 1024L;

        RestoreWindowPlacement(settings);
        RefreshPresetBar();
    }

    private void RestoreWindowPlacement(AppSettings settings)
    {
        if (settings.WindowWidth >= MinWidth) Width = settings.WindowWidth;
        if (settings.WindowHeight >= MinHeight) Height = settings.WindowHeight;

        // 前回のモニタが外れている場合に、画面外へ復元してしまわないよう確認する
        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {

            bool onScreen =
                left + 100 > SystemParameters.VirtualScreenLeft &&
                top + 40 > SystemParameters.VirtualScreenTop &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void CaptureSettings()
    {
        _settings.BrushSize = (float)SizeSlider.Value;
        _settings.BrushOpacity = (float)(OpacitySlider.Value / 100.0);
        _settings.BrushColor = ColorUtil.ToHex(Canvas.Brush.Color);
        _settings.FillTolerance = (int)ToleranceSlider.Value;
        _settings.FillExpand = (int)FillExpandSlider.Value;

        _settings.WindowMaximized = WindowState == WindowState.Maximized;

        // 最大化中は元のサイズが取れないので、そのときだけ保存しない
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }
    }

    // ===== ブラシプリセット =====

    private void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("プリセット名", $"ブラシ {_settings.Presets.Count + 1}") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _settings.Presets.Add(new BrushPreset
        {
            Name = dialog.Value,
            Kind = Canvas.Brush.Kind.ToString(),
            Size = Canvas.Brush.Size,
            Opacity = Canvas.Brush.Opacity,
            Color = ColorUtil.ToHex(Canvas.Brush.Color)
        });

        SettingsStore.Save(_settings);
        RefreshPresetBar();
    }

    private void ApplyPreset(BrushPreset preset)
    {
        SizeSlider.Value = Math.Clamp(preset.Size, SizeSlider.Minimum, SizeSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(preset.Opacity * 100.0, 0, 100);

        if (ColorUtil.TryParseHex(preset.Color, out var color))
            ApplyBrushColor(color);

        SelectTool(preset.Kind switch
        {
            "Pencil" => "Pencil",
            "Eraser" => "Eraser",
            _ => "Pen"
        });
    }

    private void RefreshPresetBar()
    {
        if (PresetPanel is null) return;

        PresetPanel.Children.Clear();

        if (_settings.Presets.Count == 0)
        {
            PresetPanel.Children.Add(new TextBlock
            {
                Text = "（右の「＋ 今のブラシを登録」で追加できます）",
                Opacity = 0.4,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        foreach (var preset in _settings.Presets)
            PresetPanel.Children.Add(CreatePresetButton(preset));
    }

    private FrameworkElement CreatePresetButton(BrushPreset preset)
    {
        ColorUtil.TryParseHex(preset.Color, out var color);
        var brush = new SolidColorBrush(ColorUtil.ToWpf(color));

        // 丸の大きさでブラシの太さが一目で分かるようにする
        double dot = Math.Clamp(preset.Size / 200.0 * 20.0 + 4.0, 4.0, 24.0);

        var shape = new System.Windows.Shapes.Ellipse
        {
            Width = dot,
            Height = dot,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = Math.Max(0.15, preset.Opacity)
        };

        if (preset.Kind == "Eraser")
        {
            shape.Stroke = new SolidColorBrush(Colors.White);
            shape.StrokeThickness = 2;
        }
        else
        {
            shape.Fill = brush;
        }

        var button = new Button
        {
            Width = 34,
            Height = 28,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(0),
            Content = shape,
            Cursor = Cursors.Hand,
            ToolTip = $"{preset.Name}" + Environment.NewLine +
                      $"サイズ {preset.Size:0} / 不透明度 {preset.Opacity * 100:0}%" + Environment.NewLine +
                      "右クリックで削除"
        };

        button.Click += (_, _) => ApplyPreset(preset);

        button.MouseRightButtonUp += (_, _) =>
        {
            var answer = MessageBox.Show(this, $"プリセット「{preset.Name}」を削除しますか？",
                                         "AINE Paint", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            _settings.Presets.Remove(preset);
            SettingsStore.Save(_settings);
            RefreshPresetBar();
        };

        return button;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _history.Dispose();
        _document?.Dispose();
    }

    // ===== ファイル =====

    private void OnNewCanvasClick(object sender, RoutedEventArgs e) => CreateNewCanvas();

    private void CreateNewCanvas()
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new NewCanvasDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        SetDocument(new PaintDocument(dialog.CanvasWidth, dialog.CanvasHeight, dialog.BackgroundMode), null);
    }

    /// <summary>ドキュメントを差し替える。履歴・レイヤー一覧・タイトルもここで揃える。</summary>
    private void SetDocument(PaintDocument document, string? path)
    {
        Canvas.CancelTransform();

        _document?.Dispose();
        _document = document;

        // 前のキャンバスの履歴は意味を持たないので捨てる
        _history.Clear();

        document.StructureChanged += RefreshLayerPanel;

        Canvas.Document = document;
        Canvas.Selection.Clear();
        EmptyHint.Visibility = Visibility.Collapsed;

        _currentPath = path;
        _isDirty = false;

        RefreshLayerPanel();
        UpdateStatus();
        UpdateTitle();
    }

    // ===== 開く / 保存 =====

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Title = "開く",
            Filter = $"対応ファイル (*.ainpaint;*.png;*.jpg;*.jpeg)|*.ainpaint;*.png;*.jpg;*.jpeg|" +
                     $"{ProjectFile.FileFilter}|{ImageFile.OpenFilter}"
        };

        if (dialog.ShowDialog(this) != true) return;

        OpenPath(dialog.FileName);
    }

    /// <summary>
    /// パスを指定して開く。ダイアログからも、関連付けからの起動からも使う。
    /// 拡張子でプロジェクトか画像かを判断する。
    /// </summary>
    public void OpenPath(string path)
    {
        try
        {
            bool isProject = string.Equals(Path.GetExtension(path),
                                           ProjectFile.Extension, StringComparison.OrdinalIgnoreCase);

            if (isProject)
                SetDocument(ProjectFile.Load(path), path);
            else
                // 画像から始めた場合は上書き保存先を持たせない（元画像を壊さないため）
                SetDocument(ImageFile.Import(path), null);
        }
        catch (Exception ex)
        {
            ShowError("開けませんでした。", ex);
        }
    }

    /// <summary>
    /// 関連付けやドラッグ＆ドロップで渡されたファイルを開く。
    /// 起動直後に呼ばれるので、失敗しても起動そのものは妨げない。
    /// </summary>
    private void OpenFromCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return;

            string path = args[1];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            OpenPath(path);
        }
        catch
        {
            // 引数がおかしくても、空の状態で起動できれば十分
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();

    private void OnSaveAsClick(object sender, RoutedEventArgs e) => SaveAs();

    /// <summary>保存できたら true。キャンセルや失敗なら false。</summary>
    private bool Save()
    {
        if (Canvas.IsTransforming) Canvas.CommitTransform();
        if (_document is null) return false;
        if (_currentPath is null) return SaveAs();

        try
        {
            ProjectFile.Save(_document, _currentPath);
            _isDirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            ShowError("保存できませんでした。", ex);
            return false;
        }
    }

    private bool SaveAs()
    {
        if (_document is null) return false;

        var dialog = new SaveFileDialog
        {
            Title = "名前を付けて保存",
            Filter = ProjectFile.FileFilter,
            DefaultExt = ProjectFile.Extension,
            AddExtension = true,
            FileName = Path.GetFileName(_currentPath) ?? $"untitled{ProjectFile.Extension}"
        };

        if (dialog.ShowDialog(this) != true) return false;

        _currentPath = dialog.FileName;
        return Save();
    }

    private void OnExportPngClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "PNGで書き出し",
            Filter = ImageFile.PngFilter,
            DefaultExt = ".png",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_currentPath) is { Length: > 0 } name
                ? name + ".png"
                : "untitled.png"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImageFile.ExportPng(_document, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError("書き出せませんでした。", ex);
        }
    }

    /// <summary>変更を破棄してよいか確認する。続行してよければ true。</summary>
    private bool ConfirmDiscardChanges()
    {
        if (_document is null || !_isDirty) return true;

        var result = MessageBox.Show(
            this,
            "変更が保存されていません。保存しますか？",
            "AINE Paint",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        CaptureSettings();
        SettingsStore.Save(_settings);
    }

    private void MarkDirty()
    {
        if (_isDirty) return;
        _isDirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = _currentPath is null ? "無題" : Path.GetFileName(_currentPath);
        Title = _document is null
            ? "AINE Paint"
            : $"{(_isDirty ? "*" : "")}{name} — AINE Paint";
    }

    private void ShowError(string message, Exception ex)
    {
        MessageBox.Show(this, $"{message}\n\n{ex.Message}", "AINE Paint",
                        MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ===== 表示 =====

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Canvas.ZoomByStep(1.25f);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Canvas.ZoomByStep(1f / 1.25f);
    private void OnFitClick(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    // ===== ツール =====

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        ApplyTool(tag);
    }

    private void ApplyTool(string tag)
    {
        // Canvas は InitializeComponent 中の IsChecked 設定でも呼ばれ得るので防御する
        if (Canvas is null) return;

        // 別のツールへ移るときは、変形中なら確定してから移る
        if (tag != "Transform" && Canvas.IsTransforming)
            Canvas.CommitTransform();

        Canvas.TransformToolActive = tag == "Transform";
        Canvas.PanToolActive = tag == "Pan";
        Canvas.EyedropperActive = tag == "Picker";
        Canvas.FillToolActive = tag == "Fill";
        Canvas.SelectionMode = tag switch
        {
            "SelectRect" => SelectionTool.Rectangle,
            "SelectLasso" => SelectionTool.Lasso,
            _ => SelectionTool.None
        };

        if (BrushOptions is not null && FillOptions is not null)
        {
            bool isFill = tag == "Fill";
            BrushOptions.Visibility = isFill ? Visibility.Collapsed : Visibility.Visible;
            FillOptions.Visibility = isFill ? Visibility.Visible : Visibility.Collapsed;
        }

        if (tag == "Transform")
        {
            if (!Canvas.Selection.IsActive)
            {
                MessageBox.Show(this,
                    "先に選択ツール（長方形 M / なげなわ L）で範囲を選んでください。",
                    "AINE Paint", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Canvas.BeginTransform();
            }
        }

        switch (tag)
        {
            case "Pen":
                Canvas.Brush.Kind = BrushKind.Pen;
                break;
            case "Pencil":
                Canvas.Brush.Kind = BrushKind.Pencil;
                break;
            case "Eraser":
                Canvas.Brush.Kind = BrushKind.Eraser;
                break;
        }
    }

    /// <summary>キーボードからツールを切り替える。ボタンの選択状態も合わせる。</summary>
    private void SelectTool(string tag)
    {
        if (ToolPanel is null) return;

        foreach (var child in ToolPanel.Children)
        {
            if (child is RadioButton { Tag: string t } button && t == tag && button.IsEnabled)
            {
                button.IsChecked = true;   // Checked イベント経由で ApplyTool が走る
                return;
            }
        }
    }

    // ===== Undo / Redo =====

    private void OnBeforeDocumentChange(SKRect rect)
    {
        if (_document?.ActiveLayer is not { } layer) return;
        _history.CapturePixels(_document, layer, rect, "ブラシ");
        MarkDirty();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => PerformUndo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => PerformRedo();

    private void PerformUndo()
    {
        if (_document is null || !_history.CanUndo) return;
        _history.Undo(_document);
        MarkDirty();
        RefreshLayerPanel();
        Canvas.InvalidateVisual();
    }

    private void PerformRedo()
    {
        if (_document is null || !_history.CanRedo) return;
        _history.Redo(_document);
        MarkDirty();
        RefreshLayerPanel();
        Canvas.InvalidateVisual();
    }

    private void UpdateHistoryMenu()
    {
        if (UndoMenuItem is null || RedoMenuItem is null) return;
        UndoMenuItem.IsEnabled = _history.CanUndo;
        RedoMenuItem.IsEnabled = _history.CanRedo;
    }

    // ===== 選択範囲 =====

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        Canvas.Selection.SelectAll(_document.Width, _document.Height);
    }

    private void OnDeselectClick(object sender, RoutedEventArgs e)
    {
        if (Canvas.IsTransforming) Canvas.CommitTransform();
        Canvas.Selection.Clear();
    }

    // ===== レイヤー =====

    private void OnAddLayerClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        _history.CaptureStructure(_document, "レイヤーを追加");
        _document.AddLayer();
        MarkDirty();
    }

    private void OnDuplicateLayerClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        _history.CaptureStructure(_document, "レイヤーを複製");
        _document.DuplicateActiveLayer();
        MarkDirty();
    }

    private void OnDeleteLayerClick(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;

        if (_document.Layers.Count <= 1)
        {
            MessageBox.Show(this, "最後のレイヤーは削除できません。", "AINE Paint",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _history.CaptureStructure(_document, "レイヤーを削除");
        if (!_document.RemoveActiveLayer())
            PerformUndo();   // 実際には消せなかったので記録を取り消す
        else
            MarkDirty();
    }

    private void OnMoveLayerUpClick(object sender, RoutedEventArgs e) => MoveActiveLayer(1);
    private void OnMoveLayerDownClick(object sender, RoutedEventArgs e) => MoveActiveLayer(-1);

    private void MoveActiveLayer(int offset)
    {
        if (_document is null) return;

        _history.CaptureStructure(_document, "レイヤーの並び替え");
        if (!_document.MoveActiveLayer(offset))
            PerformUndo();
        else
            MarkDirty();
    }

    private void OnLayerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLayers || _document is null) return;
        if (LayerList.SelectedIndex < 0) return;

        // 一覧は上下を反転して見せているので、添字も反転する
        _document.ActiveLayerIndex = _document.Layers.Count - 1 - LayerList.SelectedIndex;
        UpdateLayerOpacityControl();
    }

    private void OnLayerListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_document?.ActiveLayer is not { } layer) return;

        var dialog = new TextInputDialog("レイヤー名", layer.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        layer.Name = dialog.Value;
        MarkDirty();
        RefreshLayerPanel();
    }

    private void OnLayerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LayerOpacityText is not null)
            LayerOpacityText.Text = $"{(int)e.NewValue}%";

        if (_syncingLayers || _document?.ActiveLayer is not { } layer) return;
        layer.Opacity = (float)(e.NewValue / 100.0);
        MarkDirty();
    }

    private void RefreshLayerPanel()
    {
        if (LayerList is null) return;

        _syncingLayers = true;
        try
        {
            if (_document is null)
            {
                LayerList.ItemsSource = null;
                return;
            }

            // 一番上のレイヤーを一覧の先頭に見せる
            var reversed = _document.Layers.Reverse().ToList();
            LayerList.ItemsSource = reversed;
            LayerList.SelectedIndex = _document.Layers.Count - 1 - _document.ActiveLayerIndex;
        }
        finally
        {
            _syncingLayers = false;
        }

        UpdateLayerOpacityControl();
    }

    private void UpdateLayerOpacityControl()
    {
        if (LayerOpacitySlider is null) return;

        _syncingLayers = true;
        try
        {
            LayerOpacitySlider.IsEnabled = _document?.ActiveLayer is not null;
            if (_document?.ActiveLayer is { } layer)
                LayerOpacitySlider.Value = Math.Round(layer.Opacity * 100.0);
        }
        finally
        {
            _syncingLayers = false;
        }
    }

    // ===== 色 =====

    private void OnColorButtonClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(Canvas.Brush.Color) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        ApplyBrushColor(dialog.SelectedColor);
    }

    private void ApplyBrushColor(SKColor color)
    {
        if (Canvas is null || ColorButton is null) return;

        Canvas.Brush.Color = color;
        ColorButton.Background = new SolidColorBrush(ColorUtil.ToWpf(color));
    }

    // ===== ブラシ設定 =====

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || SizeValueText is null) return;

        Canvas.Brush.Size = (float)e.NewValue;
        SizeValueText.Text = ((int)e.NewValue).ToString();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || OpacityValueText is null) return;

        Canvas.Brush.Opacity = (float)(e.NewValue / 100.0);
        OpacityValueText.Text = $"{(int)e.NewValue}%";
    }

    private void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || ToleranceValueText is null) return;

        Canvas.Brush.FillTolerance = (int)e.NewValue;
        ToleranceValueText.Text = ((int)e.NewValue).ToString();
    }

    private void OnFillExpandChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || FillExpandValueText is null) return;

        Canvas.Brush.FillExpand = (int)e.NewValue;
        FillExpandValueText.Text = ((int)e.NewValue).ToString();
    }

    private void NudgeBrushSize(double delta)
    {
        SizeSlider.Value = Math.Clamp(SizeSlider.Value + delta, SizeSlider.Minimum, SizeSlider.Maximum);
    }

    // ===== 入力 =====

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z:
                    if (shift) PerformRedo(); else PerformUndo();
                    e.Handled = true;
                    return;
                case Key.Y:
                    PerformRedo();
                    e.Handled = true;
                    return;
                case Key.N:
                    CreateNewCanvas();
                    e.Handled = true;
                    return;
                case Key.O:
                    OnOpenClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.S:
                    if (shift) SaveAs(); else Save();
                    e.Handled = true;
                    return;
                case Key.E:
                    OnExportPngClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.A:
                    OnSelectAllClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.D:
                    OnDeselectClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.OemPlus:
                case Key.Add:
                    Canvas.ZoomByStep(1.25f);
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    Canvas.ZoomByStep(1f / 1.25f);
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    Canvas.FitToWindow();
                    e.Handled = true;
                    return;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.P: SelectTool("Pen"); e.Handled = true; return;
            case Key.N: SelectTool("Pencil"); e.Handled = true; return;
            case Key.E: SelectTool("Eraser"); e.Handled = true; return;
            case Key.H: SelectTool("Pan"); e.Handled = true; return;
            case Key.I: SelectTool("Picker"); e.Handled = true; return;
            case Key.G: SelectTool("Fill"); e.Handled = true; return;
            case Key.M: SelectTool("SelectRect"); e.Handled = true; return;
            case Key.L: SelectTool("SelectLasso"); e.Handled = true; return;

            case Key.OemOpenBrackets:
                NudgeBrushSize(-Math.Max(1, SizeSlider.Value * 0.1));
                e.Handled = true;
                return;
            case Key.OemCloseBrackets:
                NudgeBrushSize(Math.Max(1, SizeSlider.Value * 0.1));
                e.Handled = true;
                return;

            case Key.T: SelectTool("Transform"); e.Handled = true; return;

            case Key.Enter:
                if (Canvas.IsTransforming) { Canvas.CommitTransform(); e.Handled = true; }
                return;

            case Key.Escape:
                if (Canvas.IsTransforming) { Canvas.CancelTransform(); e.Handled = true; }
                return;

            case Key.Space:
                Canvas.IsPanModifierDown = true;
                e.Handled = true;   // ボタンにフォーカスがある場合の誤爆を防ぐ
                return;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Space)
            Canvas.IsPanModifierDown = false;
    }

    // ===== ステータスバー =====

    private void UpdateStatus()
    {
        if (StatusText is null) return;

        if (_document is null)
        {
            StatusText.Text = "キャンバスなし";
            return;
        }

        string background = _document.Background == CanvasBackground.Transparent ? "透明" : "白";
        StatusText.Text = $"{_document.Width} × {_document.Height} px　背景: {background}　" +
                          $"ズーム: {Canvas.Viewport.Scale * 100:0}%";
    }
}
