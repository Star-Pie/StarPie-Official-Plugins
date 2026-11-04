using System.Text;
using System.Text.Json;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class CustomKeyBinding
{
    public string Label { get; set; } = "";

    public string ActionId { get; set; } = "";

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class CustomKeyboardScheme
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<CustomKeyBinding?> Keys { get; set; } = new();
}

internal sealed class KeyboardSchemes
{
    internal const int SlotCount = 12;
    internal const int MaxSchemeCount = 32;
    internal const int MaxSchemeNameLength = 64;
    internal const int MaxParameterValueLength = 8192;
    internal const int MaxJsonBytes = 1024 * 1024;

    internal const string ErrorEmptyJson = "EMPTY_JSON";
    internal const string ErrorJsonTooLarge = "JSON_TOO_LARGE";
    internal const string ErrorInvalidJson = "INVALID_JSON";
    internal const string ErrorSchemesNull = "SCHEMES_NULL";
    internal const string ErrorSchemeNull = "SCHEME_NULL";
    internal const string ErrorTooManySchemes = "TOO_MANY_SCHEMES";
    internal const string ErrorEmptySchemeId = "EMPTY_SCHEME_ID";
    internal const string ErrorDuplicateSchemeId = "DUPLICATE_SCHEME_ID";
    internal const string ErrorEmptySchemeName = "EMPTY_SCHEME_NAME";
    internal const string ErrorSchemeNameTooLong = "SCHEME_NAME_TOO_LONG";
    internal const string ErrorSlotCount = "SLOT_COUNT";
    internal const string ErrorParameterValueTooLong = "PARAMETER_VALUE_TOO_LONG";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 32,
    };

    public List<CustomKeyboardScheme> Items { get; set; } = new();

    public static KeyboardSchemes CreateDefault(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A scheme name is required.", nameof(name));
        }

        if (name.Length > MaxSchemeNameLength)
        {
            throw new ArgumentException(
                $"A scheme name cannot exceed {MaxSchemeNameLength} characters.",
                nameof(name));
        }

        var keys = new List<CustomKeyBinding?>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            keys.Add(null);
        }

        return new KeyboardSchemes
        {
            Items =
            [
                new CustomKeyboardScheme
                {
                    Id = "scheme-" + Guid.NewGuid().ToString("N"),
                    Name = name,
                    Keys = keys,
                },
            ],
        };
    }

    public KeyboardSchemes DeepClone()
    {
        var clone = new KeyboardSchemes();
        if (Items == null)
        {
            return clone;
        }

        foreach (CustomKeyboardScheme? scheme in Items)
        {
            if (scheme == null)
            {
                continue;
            }

            var schemeCopy = new CustomKeyboardScheme
            {
                Id = scheme.Id ?? "",
                Name = scheme.Name ?? "",
                Keys = new List<CustomKeyBinding?>(),
            };

            if (scheme.Keys != null)
            {
                foreach (CustomKeyBinding? binding in scheme.Keys)
                {
                    if (binding == null)
                    {
                        schemeCopy.Keys.Add(null);
                        continue;
                    }

                    schemeCopy.Keys.Add(new CustomKeyBinding
                    {
                        Label = binding.Label ?? "",
                        ActionId = binding.ActionId ?? "",
                        Parameters = binding.Parameters == null
                            ? new Dictionary<string, string>(StringComparer.Ordinal)
                            : new Dictionary<string, string>(binding.Parameters, StringComparer.Ordinal),
                    });
                }
            }

            clone.Items.Add(schemeCopy);
        }

        return clone;
    }

    public bool TryValidate(out string? error)
    {
        if (Items == null)
        {
            error = ErrorSchemesNull;
            return false;
        }

        if (Items.Count > MaxSchemeCount)
        {
            error = ErrorTooManySchemes;
            return false;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomKeyboardScheme? scheme in Items)
        {
            if (scheme == null)
            {
                error = ErrorSchemeNull;
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheme.Id))
            {
                error = ErrorEmptySchemeId;
                return false;
            }

            if (!ids.Add(scheme.Id))
            {
                error = ErrorDuplicateSchemeId;
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheme.Name))
            {
                error = ErrorEmptySchemeName;
                return false;
            }

            if (scheme.Name.Length > MaxSchemeNameLength)
            {
                error = ErrorSchemeNameTooLong;
                return false;
            }

            if (scheme.Keys == null || scheme.Keys.Count != SlotCount)
            {
                error = ErrorSlotCount;
                return false;
            }

            foreach (CustomKeyBinding? binding in scheme.Keys)
            {
                if (binding?.Parameters == null)
                {
                    continue;
                }

                foreach (string value in binding.Parameters.Values)
                {
                    if (value != null && value.Length > MaxParameterValueLength)
                    {
                        error = ErrorParameterValueTooLong;
                        return false;
                    }
                }
            }
        }

        error = null;
        return true;
    }

    public string Serialize()
    {
        if (!TryValidate(out string? error))
        {
            throw new InvalidOperationException(error);
        }

        string json = JsonSerializer.Serialize(this, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            throw new InvalidOperationException(ErrorJsonTooLarge);
        }

        return json;
    }

    public static bool TryDeserialize(
        string? json,
        out KeyboardSchemes? schemes,
        out string? error)
    {
        schemes = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = ErrorEmptyJson;
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            error = ErrorJsonTooLarge;
            return false;
        }

        try
        {
            schemes = JsonSerializer.Deserialize<KeyboardSchemes>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = ErrorInvalidJson;
            return false;
        }
        catch (NotSupportedException)
        {
            error = ErrorInvalidJson;
            return false;
        }

        if (schemes == null)
        {
            error = ErrorInvalidJson;
            return false;
        }

        schemes.NormalizeAfterDeserialize();
        if (!schemes.TryValidate(out error))
        {
            schemes = null;
            return false;
        }

        return true;
    }

    private void NormalizeAfterDeserialize()
    {
        if (Items == null)
        {
            return;
        }

        foreach (CustomKeyboardScheme? scheme in Items)
        {
            if (scheme?.Keys == null)
            {
                continue;
            }

            foreach (CustomKeyBinding? binding in scheme.Keys)
            {
                if (binding == null)
                {
                    continue;
                }

                binding.Label ??= "";
                binding.ActionId ??= "";
                binding.Parameters ??= new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
    }
}
