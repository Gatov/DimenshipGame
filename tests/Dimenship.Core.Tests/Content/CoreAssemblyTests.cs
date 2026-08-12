using System.Reflection;
using Dimenship.Core.Simulation;
using NUnit.Framework;

namespace Dimenship.Core.Tests.Content;

/// <summary>
/// What <c>Dimenship.Core</c> is allowed to contain, asserted rather than remembered. Both rules
/// here fail quietly if left to discipline: a float creeps in through one convenient average, and
/// a Godot reference through one convenient type, and neither is noticed until the day it has to
/// come out again.
/// </summary>
public class CoreAssemblyTests
{
    private static Assembly Core => typeof(SimulationEngine).Assembly;

    [Test]
    public void TheKernel_ReferencesNeitherGodotNorTheShell()
    {
        var forbidden = Core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Dimenship.Shell", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(forbidden, Is.Empty);
    }

    [Test]
    public void TheKernel_ContainsNoFloatOrDouble()
    {
        // Ratios are permille integers, and every quantity is in milli-units. A float in the
        // kernel is a determinism bug waiting for a platform difference: two machines replaying
        // the same tick have to reach the same number, and floating point does not promise that.
        var offenders = new List<string>();

        foreach (var type in Core.GetTypes())
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsFloating(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsFloating(property.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            {
                if (IsFloating(method.ReturnType))
                {
                    offenders.Add($"{type.FullName}.{method.Name} returns {method.ReturnType.Name}");
                }

                foreach (var parameter in method.GetParameters())
                {
                    if (IsFloating(parameter.ParameterType))
                    {
                        offenders.Add($"{type.FullName}.{method.Name}({parameter.Name})");
                    }
                }
            }
        }

        Assert.That(offenders, Is.Empty);
    }

    private static bool IsFloating(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        if (bare.IsArray)
        {
            bare = bare.GetElementType()!;
        }

        return bare == typeof(float) || bare == typeof(double);
    }
}
