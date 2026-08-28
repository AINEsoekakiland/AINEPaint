using System.IO;
using System.Text;
using System.Text.Json;

namespace AINEPaint.Settings;

/// <summary>
/// 設定の読み書き。
///
/// 設定は「あれば便利」なものなので、読み書きに失敗しても
/// 例外を投げずに既定値で動き続ける。設定ファイルの破損で
/// アプリが起動しなくなるのが一番まずい。
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 数値がおかしくても保存だけは通るようにしておく（保険）
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>最後に保存が失敗した理由。成功していれば null。</summary>
    public static string? LastError { get; private set; }

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StudioAINE", "AINE Paint");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch
        {
            // 壊れていたら既定値で立ち上げる
            return new AppSettings();
        }
    }

    /// <summary>保存できたら true。失敗しても例外は投げない（理由は LastError に残す）。</summary>
    public static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            // 先に文字列化する。ここで失敗した場合、既存ファイルには一切触れない。
            string json = JsonSerializer.Serialize(settings, Options);

            // 書き込み中に落ちても既存の設定を壊さないよう、一時ファイル経由で差し替える
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            // 保存できなくても描画には影響しないのでアプリは止めない。
            // ただし黙って消すと不具合に気づけないので理由は残す。
            LastError = ex.Message;
            return false;
        }
    }
}
