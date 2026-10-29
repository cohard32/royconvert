namespace RoyConvert.Core;

/// <summary>Why a file cannot get its new name.</summary>
public enum RenameProblem
{
    None,
    /// <summary>The pattern gives an empty name, or one ending in a dot or space.</summary>
    EmptyName,
    /// <summary>The name holds a character Windows does not allow (\ / : * ? " &lt; > |).</summary>
    InvalidCharacter,
    /// <summary>Another selected file gets the same name.</summary>
    Duplicate,
    /// <summary>A file that is not being renamed already has the name.</summary>
    Exists,
}

/// <param name="Source">Full path of the file as it is.</param>
/// <param name="Target">Full path it gets; equal to <paramref name="Source"/> when the name does not change.</param>
public sealed record RenameItem(string Source, string Target, RenameProblem Problem)
{
    public bool Changes => !string.Equals(Source, Target, StringComparison.Ordinal);
}

/// <summary>A file could not be renamed; every rename already made was undone.</summary>
/// <param name="path">The file as the user knows it (its name before the rename).</param>
public sealed class RenameException(string path, Exception inner) : IOException(inner.Message, inner)
{
    private const int SharingViolation = unchecked((int)0x80070020);

    public string Path { get; } = path;

    /// <summary>Another program has the file open.</summary>
    public bool InUse => InnerException?.HResult == SharingViolation;
}

/// <summary>
/// Gives the selected files new names from a pattern: <see cref="NameToken"/> is the old name without its extension,
/// <see cref="NumberToken"/> the position in file manager order counted from a start number, padded to the width of the
/// last number ("01".."12"). The extension is kept, and each file stays in its folder.
/// </summary>
public static class BatchRename
{
    public const string NameToken = "{name}";
    public const string NumberToken = "{n}";

    private static readonly char[] InvalidCharacters = Path.GetInvalidFileNameChars();

    /// <summary>The files in the order they are numbered, each with its new name and any problem with it.</summary>
    public static IReadOnlyList<RenameItem> Plan(IEnumerable<string> files, string pattern, int start = 1)
    {
        var sources = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(NaturalComparer.Instance).ToList();
        var digits = (start + sources.Count - 1).ToString().Length;
        var items = new List<RenameItem>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var name = pattern.Replace(NameToken, Path.GetFileNameWithoutExtension(source), StringComparison.OrdinalIgnoreCase)
                .Replace(NumberToken, (start + i).ToString().PadLeft(digits, '0'), StringComparison.OrdinalIgnoreCase)
                .Trim();
            var problem = name.Length == 0 || name.EndsWith('.') ? RenameProblem.EmptyName
                : name.IndexOfAny(InvalidCharacters) >= 0 ? RenameProblem.InvalidCharacter
                : RenameProblem.None;
            var target = problem == RenameProblem.None
                ? Path.Combine(Path.GetDirectoryName(source)!, name + Path.GetExtension(source))
                : source;
            items.Add(new RenameItem(source, target, problem));
        }

        // Windows file names ignore case: "A.jpg" and "a.jpg" are the same file.
        var selected = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        var duplicates = items.Where(item => item.Problem == RenameProblem.None)
            .GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items.Select(item => item.Problem != RenameProblem.None ? item
            : duplicates.Contains(item.Target) ? item with { Problem = RenameProblem.Duplicate }
            : !selected.Contains(item.Target) && Path.Exists(item.Target) ? item with { Problem = RenameProblem.Exists }
            : item).ToList();
    }

    /// <summary>
    /// Renames every file whose name changes. Each first gets a temporary name, so files can swap names
    /// ("1.jpg" ↔ "2.jpg") and a change of case alone works. If any rename fails, the ones already made are undone.
    /// </summary>
    /// <exception cref="ArgumentException">An item has a problem.</exception>
    /// <exception cref="RenameException">A file could not be renamed (in use, gone, no permission); nothing stays renamed.</exception>
    public static void Apply(IReadOnlyList<RenameItem> items)
    {
        if (items.FirstOrDefault(item => item.Problem != RenameProblem.None) is { } bad)
        {
            throw new ArgumentException($"{bad.Source}: {bad.Problem}", nameof(items));
        }

        var changes = items.Where(item => item.Changes).ToList();
        var temporary = changes.Select(item => Path.Combine(Path.GetDirectoryName(item.Source)!,
            $".royconvert-{Guid.NewGuid():N}{Path.GetExtension(item.Source)}")).ToList();
        // Every move done so far, to undo them in reverse order.
        var done = new List<(string From, string To)>();
        var current = string.Empty;
        try
        {
            for (var i = 0; i < changes.Count; i++)
            {
                current = changes[i].Source;
                Move(changes[i].Source, temporary[i], done);
            }

            for (var i = 0; i < changes.Count; i++)
            {
                current = changes[i].Source;
                Move(temporary[i], changes[i].Target, done);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.Move(done[i].To, done[i].From);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    // Keep undoing the rest; the file keeps the name it has.
                }
            }

            throw new RenameException(current, e);
        }
    }

    private static void Move(string from, string to, List<(string From, string To)> done)
    {
        File.Move(from, to);
        done.Add((from, to));
    }
}
