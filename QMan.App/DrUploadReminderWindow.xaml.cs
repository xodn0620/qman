using System.Windows;

namespace QMan.App;

public partial class DrUploadReminderWindow : Window
{
    public bool DoNotShowAgain => DoNotShowAgainCheck.IsChecked == true;

    public DrUploadReminderWindow()
    {
        InitializeComponent();
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
