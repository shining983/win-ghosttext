namespace WinGhostText.Core;

/// <summary>
/// Small redistributable proper-noun table (days/months, places, brands, names).
/// Used to suppress false spell flags and to suggest title casing.
/// </summary>
public sealed class ProperNounLexicon
{
    private readonly Dictionary<string, string> _canonical;

    public ProperNounLexicon(IEnumerable<string> canonicalForms)
    {
        _canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var form in canonicalForms)
        {
            var trimmed = form.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Keep the first canonical spelling if duplicates appear.
            if (!_canonical.ContainsKey(trimmed))
            {
                _canonical[trimmed] = trimmed;
            }
        }
    }

    public int Count => _canonical.Count;

    /// <summary>Canonical title-case forms as listed in the embedded table.</summary>
    public IEnumerable<string> CanonicalForms => _canonical.Values;

    public bool TryGetCanonical(string typed, out string canonical) =>
        _canonical.TryGetValue(typed, out canonical!);

    public bool Contains(string typed) => _canonical.ContainsKey(typed);

    /// <summary>
    /// When the typed token matches a proper noun ignoring case but is not already
    /// in the canonical form (e.g. all-lowercase), returns the canonical spelling.
    /// </summary>
    public string? SuggestCasing(string typed)
    {
        if (string.IsNullOrEmpty(typed) || !TryGetCanonical(typed, out var canonical))
        {
            return null;
        }

        return string.Equals(typed, canonical, StringComparison.Ordinal)
            ? null
            : canonical;
    }
}
