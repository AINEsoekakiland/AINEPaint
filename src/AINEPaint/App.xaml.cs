using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AINEPaint;

public partial class App : Application
{
    /// <summary>
    /// 落ちたときの記録先。
    /// WPF は WinExe なので、未処理例外のメッセージはコンソールに出ない。
    /// 何も分からないまま終了するのを防ぐため、必ずファイルに残す。
    /// </summary>
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AINEPaint", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception, "UI スレッド");
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Report(ex, "その他のスレッド");
        };
    }

    private static void Report(Exception ex, string where)
    {
        string text;

        try
        {
            var log = new StringBuilder();
            log.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ({where}) ===");
            log.AppendLine(ex.ToString());
            log.AppendLine();

            text = log.ToString();

            var directory = Path.GetDirectoryName(LogPath);
            if (directory is not null) Directory.CreateDirectory(directory);

            File.AppendAllText(LogPath, text);
        }
        catch
        {
            text = ex.ToString();   // 記録にすら失敗した場合は、せめて画面に出す
        }

        try
        {
            MessageBox.Show(
                text.Length > 2000 ? text[..2000] : text,
                "AINE Paint — エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // ここで失敗しても、もうできることはない
        }
    }
}
