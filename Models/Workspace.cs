using Dwalia.Managers;

namespace Dwalia.Models;

public class Workspace
{
    public int Id { get; init; }
    public string Name { get; set; }
    public List<ManagedWindow> Windows { get; } = new();
    public LayoutType Layout { get; set; } = LayoutType.MasterStack;

    public Workspace(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
