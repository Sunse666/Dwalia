using YamlDotNet.Serialization;

namespace Dwalia.Configuration;

public class WidgetConfig
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "";

    [YamlMember(Alias = "bar_page")]
    public string BarPage { get; set; } = "All";

    [YamlMember(Alias = "align")]
    public string Align { get; set; } = "right";

    [YamlMember(Alias = "order")]
    public int Order { get; set; } = 0;

    [YamlMember(Alias = "width")]
    public int Width { get; set; } = 0;

    [YamlMember(Alias = "height")]
    public int Height { get; set; } = 22;

    [YamlMember(Alias = "pill_color")]
    public string PillColor { get; set; } = "";

    [YamlMember(Alias = "text_color")]
    public string TextColor { get; set; } = "";

    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;

    [YamlMember(Alias = "format")]
    public string Format { get; set; } = "";

    [YamlMember(Alias = "args")]
    public string Args { get; set; } = "";
}
