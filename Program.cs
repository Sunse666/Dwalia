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
            if (args.Length > 0)
            {
                var command = string.Join(" ", args);
                var result = Managers.IpcServer.SendCommandAndExit(command);
                Console.WriteLine(result);
            }
            else
            {
                MessageBox.Show("Dwalia is already running.", "Dwalia",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
