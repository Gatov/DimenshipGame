namespace Dimenship.Core.State.Save;

/// <summary>
/// Reads one save field at a time and reports the ones that are not there. This is what makes
/// "every DTO field is nullable" mean something: a nullable field read through <c>?? 0</c> is a
/// missing field turned into a silent default, and a stock amount defaulted to zero is a vessel
/// that quietly lost material across a load, which is the outcome the belt-drift check exists to
/// report. A save once read 94 of its fields that way.
/// <para>
/// Each call answers a usable placeholder as well as reporting, so restore can keep building the
/// world and report every gap in one load rather than stopping at the first. The placeholder never
/// reaches a running world: <see cref="WorldSave.Read"/> fails the whole load when any error was
/// collected, before the drift checks run.
/// </para>
/// <para>
/// A field the state itself holds as optional (a name override, a configured schematic, a current
/// task) is not read through here. It is absent because it is null, and absent is its value.
/// </para>
/// <para>
/// The content loader has its own helpers (<c>JsonContentSource.Required</c> and its neighbours).
/// They were not merged with these: content errors carry a file as well as a path, and content
/// enums are refused by the serializer at parse time, while the save writes its enums as strings
/// and resolves them here. A shared reader would be thin wrappers over two different error shapes.
/// </para>
/// </summary>
internal sealed class SaveFields(List<SaveError> errors)
{
    /// <summary>A required number or flag.</summary>
    public T Value<T>(T? value, string path) where T : struct
    {
        if (value is { } present)
        {
            return present;
        }

        Missing(path);
        return default;
    }

    /// <summary>A required string. Empty is a value; an id that resolves to nothing is drift, reported later.</summary>
    public string Text(string? value, string path)
    {
        if (value is not null)
        {
            return value;
        }

        Missing(path);
        return string.Empty;
    }

    /// <summary>A required list. Written even when empty, so absent is never the same as empty.</summary>
    public IReadOnlyList<T> List<T>(IReadOnlyList<T>? value, string path)
    {
        if (value is not null)
        {
            return value;
        }

        Missing(path);
        return Array.Empty<T>();
    }

    /// <summary>A required branch of the tree: an object every save writes.</summary>
    public T? Branch<T>(T? value, string path) where T : class
    {
        if (value is null)
        {
            Missing(path);
        }

        return value;
    }

    /// <summary>
    /// A required enum, written by name. Only a declared member's exact name is accepted:
    /// <see cref="System.Enum.TryParse{TEnum}(string, out TEnum)"/> alone would also take
    /// <c>"42"</c>, or a comma-joined list of names, and give back a value no member has.
    /// <see cref="System.Enum.Parse{TEnum}(string)"/> throws instead, out of a loader whose contract
    /// is to report problems, not throw them.
    /// </summary>
    public TEnum Enum<TEnum>(string? name, string path) where TEnum : struct, Enum
    {
        if (name is null)
        {
            Missing(path);
            return default;
        }

        return Named<TEnum>(name, path) ?? default;
    }

    /// <summary>An optional enum: absent is null, and present must name a declared member.</summary>
    public TEnum? OptionalEnum<TEnum>(string? name, string path) where TEnum : struct, Enum =>
        name is null ? null : Named<TEnum>(name, path);

    /// <summary>The member an exact declared name names, or null after reporting it.</summary>
    public TEnum? Named<TEnum>(string name, string path) where TEnum : struct, Enum
    {
        if (System.Enum.IsDefined(typeof(TEnum), name) && System.Enum.TryParse<TEnum>(name, out var member))
        {
            return member;
        }

        errors.Add(new SaveError(path, $"'{name}' is not a {typeof(TEnum).Name}."));
        return null;
    }

    /// <summary>Reports an array whose length disagrees with the one it runs beside.</summary>
    public void Mismatch(string path, int length, int expected) =>
        errors.Add(new SaveError(path, $"has {length} entries where {expected} are needed."));

    /// <summary>Reports an index outside the array it points into.</summary>
    public void OutOfRange(string path, long index, int length) =>
        errors.Add(new SaveError(path, $"is {index}, outside the {length} entries it indexes."));

    private void Missing(string path) => errors.Add(new SaveError(path, "is required."));
}
