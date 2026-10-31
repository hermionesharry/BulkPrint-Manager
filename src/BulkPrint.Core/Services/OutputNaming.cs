namespace BulkPrint.Core.Services;

/// <summary>Chooses file names for saved print output without ever overwriting a file.</summary>
public sealed class OutputNaming
{
    private readonly string _folder;
    private readonly string _extension;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public OutputNaming(string folder, string extension)
    {
        _folder = folder;
        _extension = extension.StartsWith('.') ? extension : "." + extension;
    }

    public string Folder => _folder;

    /// <summary>"Invoice 12.docx" becomes "Invoice 12.pdf", or "Invoice 12 (2).pdf" if that name is taken.</summary>
    public string Next(string sourceFileName)
    {
        var baseName = Clean(Path.GetFileNameWithoutExtension(sourceFileName));
        if (baseName.Length == 0) baseName = "Document";
        for (int n = 1; ; n++)
        {
            var name = n == 1 ? baseName + _extension : $"{baseName} ({n}){_extension}";
            var path = Path.Combine(_folder, name);
            if (_used.Contains(path) || File.Exists(path)) continue;
            _used.Add(path);
            return path;
        }
    }

    private static string Clean(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.');
    }
}
