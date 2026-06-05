using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Dwalia.Styling;

public enum CssSelectorType { Type, Id, Class, Attribute }

public class CssSelector
{
    public CssSelectorType Type;
    public string Value = "";
    public string? AttrValue;

    public int Specificity => Type switch
    {
        CssSelectorType.Id => 100,
        CssSelectorType.Attribute => 10,
        CssSelectorType.Class => 10,
        _ => 1
    };
}

public class CssRule
{
    public List<CssSelector[]> Selectors = new();
    public Dictionary<string, string> Declarations = new();
    public int Line;
}

public class CssStyleSheet
{
    public List<CssRule> Rules = new();
}

public static class CssParser
{
    public static CssStyleSheet Parse(string css)
    {
        var sheet = new CssStyleSheet();
        int i = 0;
        int line = 1;

        while (i < css.Length)
        {
            SkipWhitespace(css, ref i, ref line);
            if (i >= css.Length) break;

            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < css.Length && !(css[i] == '*' && css[i + 1] == '/'))
                {
                    if (css[i] == '\n') line++;
                    i++;
                }
                i += 2;
                continue;
            }

            var rule = ParseRule(css, ref i, ref line);
            if (rule != null)
                sheet.Rules.Add(rule);
        }

        return sheet;
    }

    private static CssRule? ParseRule(string css, ref int i, ref int line)
    {
        var rule = new CssRule { Line = line };
        var selectorStr = ReadUntil(css, ref i, '{');
        if (i >= css.Length) return null;

        i++;
        ParseSelectors(selectorStr.Trim(), rule);

        while (i < css.Length)
        {
            SkipWhitespace(css, ref i, ref line);
            if (i >= css.Length) break;
            if (css[i] == '}') { i++; break; }

            var prop = ReadUntil(css, ref i, ':').Trim();
            if (i >= css.Length || css[i] != ':') break;
            i++;

            var value = ReadUntil(css, ref i, ';', '}').Trim();
            if (!string.IsNullOrEmpty(prop) && !string.IsNullOrEmpty(value))
                rule.Declarations[prop] = value;

            if (i < css.Length && css[i] == ';') i++;
            if (i < css.Length && css[i] == '}') { i++; break; }
        }

        if (css[i - 1] == '\n') line++;
        return rule.Declarations.Count > 0 ? rule : null;
    }

    private static void ParseSelectors(string str, CssRule rule)
    {
        foreach (var part in SplitTopLevel(str, ','))
        {
            var selectors = new List<CssSelector>();
            foreach (var token in Tokenize(part.Trim()))
            {
                var s = ParseSingleSelector(token);
                if (s != null) selectors.Add(s);
            }
            if (selectors.Count > 0)
                rule.Selectors.Add(selectors.ToArray());
        }
    }

    private static CssSelector? ParseSingleSelector(string token)
    {
        if (token.StartsWith("#"))
            return new CssSelector { Type = CssSelectorType.Id, Value = token[1..] };
        if (token.StartsWith("."))
            return new CssSelector { Type = CssSelectorType.Class, Value = token[1..] };
        if (token.StartsWith("[") && token.EndsWith("]"))
        {
            var inner = token[1..^1];
            var eqIdx = inner.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = inner[..eqIdx].Trim();
                var val = inner[(eqIdx + 1)..].Trim().Trim('"').Trim('\'');
                if (key == "type")
                    return new CssSelector { Type = CssSelectorType.Attribute, Value = "type", AttrValue = val };
            }
            return null;
        }
        return new CssSelector { Type = CssSelectorType.Type, Value = token };
    }

    private static string[] Tokenize(string selector)
    {
        var tokens = new List<string>();
        int start = 0;
        for (int i = 0; i <= selector.Length; i++)
        {
            if (i == selector.Length || (selector[i] == ' ' && (i == 0 || selector[i - 1] != '[')))
            {
                if (i > start)
                {
                    var t = selector[start..i].Trim();
                    if (t.Length > 0) tokens.Add(t);
                }
                start = i + 1;
            }
            else if (i < selector.Length && selector[i] == '[')
            {
                while (i < selector.Length && selector[i] != ']') i++;
            }
        }
        return tokens.ToArray();
    }

    private static string[] SplitTopLevel(string str, char sep)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i <= str.Length; i++)
        {
            if (i == str.Length || (str[i] == sep && depth == 0))
            {
                var p = str[start..(i == str.Length ? i : i)].Trim();
                if (p.Length > 0) parts.Add(p);
                start = i + 1;
            }
            else if (i < str.Length && str[i] == '[') depth++;
            else if (i < str.Length && str[i] == ']') depth--;
        }
        return parts.ToArray();
    }

    private static string ReadUntil(string s, ref int i, params char[] terminators)
    {
        var set = new HashSet<char>(terminators);
        int start = i;
        while (i < s.Length && !set.Contains(s[i]))
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var before = s[start..i];
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i += 2;
                var after = ReadUntil(s, ref i, terminators);
                return (before.TrimEnd() + " " + after.TrimStart()).Trim();
            }
            i++;
        }
        return s[start..i];
    }

    private static void SkipWhitespace(string s, ref int i, ref int line)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
        {
            if (s[i] == '\n') line++;
            i++;
        }
    }

    public static Color? ParseColor(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        value = value.Trim();

        if (value.StartsWith("rgba(") && value.EndsWith(")"))
        {
            var parts = value[5..^1].Split(',');
            if (parts.Length == 4 &&
                byte.TryParse(parts[0].Trim(), out var r) &&
                byte.TryParse(parts[1].Trim(), out var g) &&
                byte.TryParse(parts[2].Trim(), out var b) &&
                float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                return Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), r, g, b);
        }
        if (value.StartsWith("rgb(") && value.EndsWith(")"))
        {
            var parts = value[4..^1].Split(',');
            if (parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out var r) &&
                byte.TryParse(parts[1].Trim(), out var g) &&
                byte.TryParse(parts[2].Trim(), out var b))
                return Color.FromArgb(255, r, g, b);
        }
        if (value.StartsWith("#"))
        {
            var hex = value[1..].Trim();
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, null, out var argb))
                return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, null, out var rgb))
                return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            if (hex.Length == 3 && uint.TryParse(hex, NumberStyles.HexNumber, null, out var rgb3))
            {
                var rr = (byte)((rgb3 >> 8) & 0xF);
                var gg = (byte)((rgb3 >> 4) & 0xF);
                var bb = (byte)(rgb3 & 0xF);
                return Color.FromRgb((byte)(rr * 17), (byte)(gg * 17), (byte)(bb * 17));
            }
        }
        return null;
    }

    public static double? ParsePx(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        value = value.Trim();
        if (value.EndsWith("px") && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            return px;
        return null;
    }

    public static Thickness? ParseThickness(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            var v = ParsePx(parts[0]);
            return v.HasValue ? new Thickness(v.Value) : null;
        }
        if (parts.Length == 2)
        {
            var v = ParsePx(parts[0]); var h = ParsePx(parts[1]);
            return v.HasValue && h.HasValue ? new Thickness(h.Value, v.Value, h.Value, v.Value) : null;
        }
        if (parts.Length == 4)
        {
            var t = ParsePx(parts[0]); var r = ParsePx(parts[1]);
            var b = ParsePx(parts[2]); var l = ParsePx(parts[3]);
            return t.HasValue && r.HasValue && b.HasValue && l.HasValue
                ? new Thickness(l.Value, t.Value, r.Value, b.Value) : null;
        }
        return null;
    }

    public static double? ParseNumber(string value)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return n;
        return null;
    }
}
