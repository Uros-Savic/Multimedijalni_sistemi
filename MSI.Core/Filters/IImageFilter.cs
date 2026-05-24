using System.Drawing;
using System.Globalization;

namespace MSI.Core.Filters;

public interface IImageFilter
{
    string Name { get; }
    Bitmap Apply(Bitmap source, FilterParameters parameters);
}
public sealed class FilterParameters
{
    private readonly Dictionary<string, string> _params;

    public FilterParameters(Dictionary<string, string>? parameters = null)
        => _params = parameters ?? new Dictionary<string, string>();

    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (!_params.TryGetValue(key, out var v))
            return defaultValue;
        string normalized = v.Trim().Replace('.', ',');
        if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out var result))
            return result;
        if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;
        string withDot = normalized.Replace(',', '.');
        if (float.TryParse(withDot, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return result;
        return defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
        => _params.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : defaultValue;

    public string GetString(string key, string defaultValue = "")
        => _params.TryGetValue(key, out var v) ? v : defaultValue;

    public bool GetBool(string key, bool defaultValue = false)
        => _params.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : defaultValue;

    public Dictionary<string, string> Raw => _params;
}