namespace Dwalia.Models;

public class MonitorData
{
    public int Id { get; init; }
    public string DeviceName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public System.Windows.Rect Bounds { get; set; }
    public System.Windows.Rect WorkArea { get; set; }
}
