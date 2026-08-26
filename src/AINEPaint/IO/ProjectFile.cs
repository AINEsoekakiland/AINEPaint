using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AINEPaint.Drawing;
using AINEPaint.Layers;
using SkiaSharp;

namespace AINEPaint.IO;

/// <summary>
/// 独自プロジェクト形式 .ainpaint の読み書き。
///
/// 中身は ZIP で、
///   project.json   … キャンバス情報とレイヤーのメタデータ
///   layers/0.png   … 一番下のレイヤーの画像（以降 1.png, 2.png …）
/// という構成。
///
/// 独自バイナリにせず ZIP + PNG にしているのは、
/// ・PNG なので圧縮が効き、透明も保てる
/// ・万一アプリが壊れても、ZIPを開けば絵を救出できる
/// ・項目を足しても古いファイルが読めなくならない
/// という3点のため。
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".ainpaint";
    public const string FileFilter = "AINE Paint プロジェクト (*.ainpaint)|*.ainpaint";

    private const string ManifestEntryName = "project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Save(PaintDocument document, string path)
    {
        var manifest = new ProjectManifest
        {
            Width = document.Width,
            Height = document.Height,
            Background = document.Background == CanvasBackground.Transparent ? "transparent" : "white",
            ActiveLayerIndex = document.ActiveLayerIndex
        };

        // 途中で失敗しても既存ファイルを壊さないよう、一時ファイルに書いてから差し替える
        string tempPath = path + ".tmp";

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (int i = 0; i < document.Layers.Count; i++)
            {
                var layer = document.Layers[i];
                string entryName = $"layers/{i}.png";

                manifest.Layers.Add(new LayerManifest
                {
                    Name = layer.Name,
                    Visible = layer.IsVisible,
                    Opacity = layer.Opacity,
                    File = entryName
                });

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var image = SKImage.FromBitmap(layer.Bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                data.SaveTo(entryStream);
            }

            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            using var writer = new StreamWriter(manifestStream, new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public static PaintDocument Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("project.json が見つかりません。AINE Paint のファイルではない可能性があります。");

        ProjectManifest manifest;
        using (var manifestStream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<ProjectManifest>(manifestStream, JsonOptions)
                       ?? throw new InvalidDataException("project.json を読み取れませんでした。");

        if (manifest.Width < PaintDocument.MinSide || manifest.Width > PaintDocument.MaxSide ||
            manifest.Height < PaintDocument.MinSide || manifest.Height > PaintDocument.MaxSide)
            throw new InvalidDataException("キャンバスサイズが対応範囲外です。");

        var background = manifest.Background == "transparent"
            ? CanvasBackground.Transparent
            : CanvasBackground.White;

        var layers = new List<Layer>();

        foreach (var layerManifest in manifest.Layers)
        {
            var entry = archive.GetEntry(layerManifest.File);
            if (entry is null) continue;

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            buffer.Position = 0;

            using var bitmap = SKBitmap.Decode(buffer);
            if (bitmap is null) continue;

            var layer = Layer.FromBitmap(bitmap, manifest.Width, manifest.Height, layerManifest.Name);
            layer.IsVisible = layerManifest.Visible;
            layer.Opacity = layerManifest.Opacity;
            layers.Add(layer);
        }

        return PaintDocument.FromLayers(manifest.Width, manifest.Height, background,
                                        layers, manifest.ActiveLayerIndex);
    }
}
