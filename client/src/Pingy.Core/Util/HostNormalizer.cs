namespace Pingy.Core.Util;

public static class HostNormalizer
{
    public static string Normalize(string input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return s;

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return uri.Host;

        var slashIdx = s.IndexOf('/');
        if (slashIdx > 0) s = s[..slashIdx];

        var colonIdx = s.IndexOf(':');
        if (colonIdx > 0) s = s[..colonIdx];

        return s;
    }

    public static string Slugify(string label)
    {
        var s = (label ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return $"target-{Guid.NewGuid().ToString("N")[..8]}";

        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length > 0 ? slug : $"target-{Guid.NewGuid().ToString("N")[..8]}";
    }
}
