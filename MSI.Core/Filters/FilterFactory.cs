namespace MSI.Core.Filters;

public static class FilterFactory
{
    private static readonly Dictionary<string, IImageFilter> _filters;

    static FilterFactory()
    {
        _filters = new Dictionary<string, IImageFilter>(StringComparer.OrdinalIgnoreCase);
        Register(new InvertFilter());
        Register(new ContrastFilter());
        Register(new MeanRemovalFilter());
        Register(new EdgeEnhanceFilter());
        Register(new SphereFilter());
        Register(new PixelateFilter());
        Register(new SierraFilter());
        Register(new CrossDomainColorizeFilter());
    }

    private static void Register(IImageFilter filter) => _filters[filter.Name] = filter;

    // uzima filter po imenu
    public static IImageFilter Get(string name)
    {
        if (_filters.TryGetValue(name, out var f)) return f;
        throw new KeyNotFoundException(
            $"Filter '{name}' nije pronadjen. Dostupni filteri: {string.Join(", ", _filters.Keys)}");
    }

    public static bool Exists(string name) => _filters.ContainsKey(name);
    public static IEnumerable<string> AllFilterNames => _filters.Keys;
}
