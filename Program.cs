using System.Windows;

namespace Dwalia;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Dwalia_WindowManager_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Dwalia is already running.", "Dwalia",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
