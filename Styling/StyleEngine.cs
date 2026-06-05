using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Dwalia.Infrastructure;

namespace Dwalia.Styling;

public static class StyleEngine
{
    private static CssStyleSheet? _currentSheet;

    public static void LoadAndApply(string cssPath)
    {
        if (!File.Exists(cssPath))
        {
            Logger.Warn($"CSS file not found: {cssPath}");
            return;
        }

        try
        {
            var css = File.ReadAllText(cssPath);
            _currentSheet = CssParser.Parse(css);
            Logger.Info($"Loaded stylesheet: {cssPath} ({_currentSheet.Rules.Count} rules)");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to parse CSS: {ex.Message}");
            _currentSheet = null;
        }
    }

    public static void ApplyToVisualTree(DependencyObject root)
    {
        if (_currentSheet == null) return;
        ApplyRecursive(root, new HashSet<DependencyObject>());
    }

    public static bool HasStyles => _currentSheet != null;

    public static void ApplyToElement(FrameworkElement fe)
    {
        if (_currentSheet == null) return;
        var ctx = new ElementContext(fe);
        var props = MatchRules(ctx);
        foreach (var (key, value) in props)
            ApplyProperty(fe, key, value);
    }

    private static void ApplyRecursive(DependencyObject obj, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(obj)) return;

        if (obj is FrameworkElement fe)
        {
            var ctx = new ElementContext(fe);
            var props = MatchRules(ctx);
            foreach (var (key, value) in props)
                ApplyProperty(fe, key, value);
        }

        int count = VisualTreeHelper.GetChildrenCount(obj);
        for (int i = 0; i < count; i++)
            ApplyRecursive(VisualTreeHelper.GetChild(obj, i), visited);
    }

    private static Dictionary<string, object> MatchRules(ElementContext ctx)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var priorities = new Dictionary<string, int>();

        foreach (var rule in _currentSheet!.Rules)
        {
            foreach (var selectorGroup in rule.Selectors)
            {
                if (!MatchesAll(selectorGroup, ctx)) continue;

                int specificity = selectorGroup.Sum(s => s.Specificity);

                foreach (var (prop, rawValue) in rule.Declarations)
                {
                    var wpfValue = ConvertValue(prop, rawValue);
                    if (wpfValue == null) continue;

                    if (!priorities.TryGetValue(prop, out var existing) || specificity >= existing)
                    {
                        priorities[prop] = specificity;
                        result[prop] = wpfValue;
                    }
                }
                break;
            }
        }
        return result;
    }

    private static bool MatchesAll(CssSelector[] selectors, ElementContext ctx)
    {
        return selectors.All(s => MatchesSingle(s, ctx));
    }

    private static bool MatchesSingle(CssSelector s, ElementContext ctx)
    {
        return s.Type switch
        {
            CssSelectorType.Id => string.Equals(ctx.Id, s.Value, StringComparison.OrdinalIgnoreCase),
            CssSelectorType.Class => ctx.HasClass(s.Value),
            CssSelectorType.Attribute => string.Equals(ctx.GetAttr(s.Value), s.AttrValue, StringComparison.OrdinalIgnoreCase),
            CssSelectorType.Type => string.Equals(ctx.TypeName, s.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static object? ConvertValue(string property, string rawValue)
    {
        return property.ToLowerInvariant() switch
        {
            "background" or "background-color" => CssParser.ParseColor(rawValue) is { } c
                ? (object)new SolidColorBrush(c) : null,
            "color" or "foreground" => CssParser.ParseColor(rawValue) is { } c
                ? (object)new SolidColorBrush(c) : null,
            "font-size" => CssParser.ParsePx(rawValue),
            "font-weight" => rawValue.Trim().ToLowerInvariant() switch
            {
                "bold" => FontWeights.Bold,
                "normal" => FontWeights.Normal,
                "light" => FontWeights.Light,
                "medium" => FontWeights.Medium,
                "semibold" => FontWeights.SemiBold,
                _ => CssParser.ParseNumber(rawValue) is { } n ? (object)n : null
            },
            "border-color" => CssParser.ParseColor(rawValue) is { } c
                ? (object)new SolidColorBrush(c) : null,
            "border-width" => CssParser.ParseThickness(rawValue),
            "border-radius" => CssParser.ParsePx(rawValue) is { } r
                ? (object)new CornerRadius(r) : null,
            "padding" => CssParser.ParseThickness(rawValue),
            "margin" => CssParser.ParseThickness(rawValue),
            "opacity" => CssParser.ParseNumber(rawValue) is { } o
                ? (object)Math.Clamp(o, 0, 1) : null,
            "width" => CssParser.ParsePx(rawValue),
            "height" => CssParser.ParsePx(rawValue),
            "min-width" => CssParser.ParsePx(rawValue),
            "min-height" => CssParser.ParsePx(rawValue),
            _ => null
        };
    }

    private static void ApplyProperty(FrameworkElement fe, string property, object? value)
    {
        if (value == null) return;
        try
        {
            switch (property.ToLowerInvariant())
            {
                case "background":
                case "background-color":
                    if (fe is Control c) c.Background = (Brush)value;
                    else if (fe is Border b) b.Background = (Brush)value;
                    else if (fe is Panel p) p.Background = (Brush)value;
                    break;
                case "color":
                case "foreground":
                    if (fe is Control c2) c2.Foreground = (Brush)value;
                    else if (fe is TextBlock tb) tb.Foreground = (Brush)value;
                    break;
                case "font-size":
                    fe.SetValue(TextElement.FontSizeProperty, (double)value);
                    break;
                case "font-weight":
                    if (value is FontWeight fw)
                        fe.SetValue(TextElement.FontWeightProperty, fw);
                    else
                        fe.SetValue(TextElement.FontWeightProperty, FontWeight.FromOpenTypeWeight((int)(double)value));
                    break;
                case "border-color":
                    if (fe is Border b2) b2.BorderBrush = (Brush)value;
                    break;
                case "border-width":
                    if (fe is Border b3) b3.BorderThickness = (Thickness)value;
                    break;
                case "border-radius":
                    if (fe is Border b4) b4.CornerRadius = (CornerRadius)value;
                    break;
                case "padding":
                    if (fe is Control c3) c3.Padding = (Thickness)value;
                    else if (fe is Border b5) b5.Padding = (Thickness)value;
                    else if (fe is TextBlock tb2) tb2.Padding = (Thickness)value;
                    break;
                case "margin":
                    fe.Margin = (Thickness)value;
                    break;
                case "opacity":
                    fe.Opacity = (double)value;
                    break;
                case "width":
                    fe.Width = (double)value;
                    break;
                case "height":
                    fe.Height = (double)value;
                    break;
                case "min-width":
                    fe.MinWidth = (double)value;
                    break;
                case "min-height":
                    fe.MinHeight = (double)value;
                    break;
            }
        }
        catch { /* skip invalid property values */ }
    }

    private class ElementContext
    {
        private readonly FrameworkElement _fe;

        public ElementContext(FrameworkElement fe) => _fe = fe;

        public string Id => _fe.Name ?? "";
        public string TypeName => _fe.GetType().Name;

        public bool HasClass(string className)
        {
            if (_fe.Tag is string tag)
                return tag.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));
            return false;
        }

        public string? GetAttr(string key)
        {
            if (key == "type" && _fe.Tag is string tag)
            {
                var parts = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var prefix = "type:";
                var match = parts.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return match?[prefix.Length..];
            }
            return null;
        }
    }
}
