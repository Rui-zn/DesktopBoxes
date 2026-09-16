namespace DesktopBoxes.Core;

/// <summary>Centralized box-name normalization and uniqueness rules.</summary>
public static class BoxNames
{
    public const string DefaultName = "新建盒子";

    public static string Normalize(string? name) => string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();

    public static bool IsAvailable(IEnumerable<Box> boxes, string? name, Guid? exceptBoxId = null)
    {
        string normalized = Normalize(name);
        return boxes.All(box => box.Id == exceptBoxId ||
            !string.Equals(Normalize(box.Name), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string CreateUnique(string? preferredName, IEnumerable<Box> boxes)
    {
        var used = boxes
            .Select(box => Normalize(box.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CreateUnique(preferredName, used);
    }

    /// <summary>Normalizes names and gives duplicates stable numeric suffixes in list order.</summary>
    public static bool EnsureUnique(IList<Box> boxes)
    {
        bool changed = false;
        string[] normalizedNames = boxes.Select(box => Normalize(box.Name)).ToArray();
        var reserved = normalizedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < boxes.Count; index++)
        {
            Box box = boxes[index];
            string normalized = normalizedNames[index];
            string unique;
            if (used.Add(normalized))
            {
                unique = normalized;
            }
            else
            {
                var unavailable = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
                unavailable.UnionWith(used);
                unique = CreateUnique(normalized, unavailable);
                used.Add(unique);
                reserved.Add(unique);
            }
            if (!string.Equals(box.Name, unique, StringComparison.Ordinal))
            {
                box.Name = unique;
                changed = true;
            }
        }
        return changed;
    }

    private static string CreateUnique(string? preferredName, ISet<string> used)
    {
        string baseName = Normalize(preferredName);
        if (!used.Contains(baseName)) return baseName;

        for (int number = 2; number < int.MaxValue; number++)
        {
            string candidate = $"{baseName} ({number})";
            if (!used.Contains(candidate)) return candidate;
        }

        throw new InvalidOperationException("无法生成唯一的盒子名称。");
    }
}
