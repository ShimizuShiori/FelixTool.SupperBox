using System.Reflection;
using System.IO;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public sealed record PluginLoadResult(
    IReadOnlyList<ICommandPlugin> Plugins,
    IReadOnlyList<string> Errors);

public static class PluginLoader
{
    public static PluginLoadResult Load(string pluginDirectory)
    {
        Directory.CreateDirectory(pluginDirectory);
        var plugins = new List<ICommandPlugin>();
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(pluginDirectory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(file);
                foreach (var type in assembly.GetTypes().Where(IsPluginType))
                {
                    try
                    {
                        plugins.Add((ICommandPlugin)Activator.CreateInstance(type)!);
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"{Path.GetFileName(file)} / {type.Name}: {exception.GetBaseException().Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(file)}: {exception.GetBaseException().Message}");
            }
        }

        var duplicates = plugins.GroupBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var duplicate in duplicates)
        {
            errors.Add($"命令名“{duplicate.Key}”重复，相关插件均未加载。");
        }

        var duplicateNames = duplicates.Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PluginLoadResult(
            plugins.Where(plugin => !duplicateNames.Contains(plugin.Name)).ToList(),
            errors);
    }

    private static bool IsPluginType(Type type)
    {
        return typeof(ICommandPlugin).IsAssignableFrom(type)
            && type is { IsClass: true, IsAbstract: false }
            && type.GetConstructor(Type.EmptyTypes) is not null;
    }
}
