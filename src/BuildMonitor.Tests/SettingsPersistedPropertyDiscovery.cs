using System.Collections;
using System.Reflection;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

/// <summary>
/// Discovers persisted settings leaf paths for coverage against <see cref="SettingsApplyImpactCatalog"/>.
/// </summary>
public static class SettingsPersistedPropertyDiscovery
{
    private static readonly HashSet<string> SkipTypeNames =
    [
        nameof(LegacyAppSettingsV20),
        nameof(LegacyFlatProjectSettings)
    ];

    public static IReadOnlyList<string> DiscoverLeafPaths()
    {
        var paths = new List<string>();
        Walk(typeof(AppSettings), prefix: "", paths);
        return paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static void Walk(Type type, string prefix, List<string> paths)
    {
        if (SkipTypeNames.Contains(type.Name))
        {
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (prop.GetIndexParameters().Length > 0 || prop.GetMethod is null)
            {
                continue;
            }

            // Computed / non-persisted
            if (!prop.CanWrite && prop.Name is nameof(MonitoredProjectSettings.ListLabel))
            {
                continue;
            }

            if (!prop.CanWrite && prop.PropertyType != typeof(string) && !IsCollection(prop.PropertyType))
            {
                // get-only non-collection (e.g. ListLabel already skipped)
                continue;
            }

            var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            var propType = prop.PropertyType;

            if (IsSimple(propType))
            {
                paths.Add(path);
                continue;
            }

            if (IsStringList(propType))
            {
                paths.Add(path + "[]");
                continue;
            }

            if (TryGetListElementType(propType, out var elementType))
            {
                var elementPrefix = path + "[]";
                if (IsSimple(elementType!))
                {
                    paths.Add(elementPrefix);
                }
                else
                {
                    Walk(elementType!, elementPrefix, paths);
                }

                continue;
            }

            Walk(propType, path, paths);
        }
    }

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum
               || type == typeof(string)
               || type == typeof(bool)
               || type == typeof(int)
               || type == typeof(long)
               || type == typeof(double)
               || type == typeof(float)
               || type == typeof(decimal);
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsStringList(Type type) =>
        TryGetListElementType(type, out var el) && el == typeof(string);

    private static bool TryGetListElementType(Type type, out Type? elementType)
    {
        elementType = null;
        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType is not null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        return false;
    }
}
