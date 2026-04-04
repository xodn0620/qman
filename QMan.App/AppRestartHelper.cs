using System.Diagnostics;
using System.Windows;

namespace QMan.App;

internal static class AppRestartHelper
{
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            MessageBox.Show("실행 파일 경로를 알 수 없어 자동 재시작할 수 없습니다. 앱을 직접 다시 실행해 주세요.",
                "Q-Man", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
