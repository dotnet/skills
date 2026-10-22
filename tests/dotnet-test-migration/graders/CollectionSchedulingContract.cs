using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

internal static class CollectionSchedulingContract
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var assembly = typeof(CollectionSchedulingContract).Assembly;
        var allMethods = assembly.GetTypes().SelectMany(type => type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)).ToArray();
        if (allMethods.Any(method => method.IsDefined(typeof(AssemblyInitializeAttribute))))
        {
            throw new InvalidOperationException(
                "Collection lifetime contract: resources must be created lazily for selected members.");
        }
        if (allMethods.Any(method => method.IsDefined(typeof(AssemblyCleanupAttribute))))
        {
            if (allMethods.Any(method => method.IsDefined(typeof(TestMethodAttribute)) &&
                method.DeclaringType != typeof(FirstMember) && method.DeclaringType != typeof(SecondMember)))
            {
                throw new InvalidOperationException(
                    "Collection lifetime contract: run-scoped cleanup cannot include unrelated tests.");
            }
            RecordLifetime("sole-collection-run");
        }
        else
        {
            RecordLifetime("member-lifecycle");
        }

        var parallel = assembly.GetCustomAttribute<ParallelizeAttribute>();
        if (assembly.IsDefined(typeof(DoNotParallelizeAttribute)) ||
            parallel is null || parallel.Workers == 1)
        {
            RecordPolicy("assembly-serial");
            return;
        }

        var members = new[] { typeof(FirstMember), typeof(SecondMember) };
        var methods = new[] { nameof(FirstMember.FirstCase), nameof(SecondMember.SecondCase) };
        var policies = members.Select((member, index) =>
            member.GetCustomAttributes(inherit: true)
                .Concat(member.GetMethod(methods[index])!.GetCustomAttributes(inherit: true))
                .ToArray()).ToArray();

        var deferred = policies.Select(attributes =>
            attributes.Any(a => a is DoNotParallelizeAttribute)).ToArray();
        if (deferred.All(value => value))
        {
            RecordPolicy("deferred-members");
            return;
        }
        if (deferred.Any(value => value))
        {
            throw new InvalidOperationException(
                "Collection scheduling contract: both members must be deferred for a group-wide deferred disclosure.");
        }

        var resources = policies.Select(attributes => attributes
            .Where(a => a.GetType().FullName ==
                "Microsoft.VisualStudio.TestTools.UnitTesting.ResourceLockAttribute")
            .Select(a => new
            {
                Name = (string)a.GetType().GetProperty("Resource")!.GetValue(a)!,
                Exclusive = a.GetType().GetProperty("Mode")!.GetValue(a)!.ToString() == "ReadWrite"
            }).ToArray()).ToArray();
        if (resources[0].Any(first => resources[1].Any(second =>
            first.Name == second.Name && (first.Exclusive || second.Exclusive))))
        {
            RecordPolicy("shared-resource");
            return;
        }

        throw new InvalidOperationException(
            "Collection scheduling contract: shared members can overlap.");
    }

    private static void RecordPolicy(string policy)
    {
        var lifecycle = Environment.GetEnvironmentVariable("MIGRATION_PROBE");
        if (!string.IsNullOrEmpty(lifecycle))
        {
            File.WriteAllText(Path.ChangeExtension(lifecycle, ".scheduling.json"), policy);
        }
    }

    private static void RecordLifetime(string lifetime)
    {
        var lifecycle = Environment.GetEnvironmentVariable("MIGRATION_PROBE");
        if (!string.IsNullOrEmpty(lifecycle))
            File.WriteAllText(Path.ChangeExtension(lifecycle, ".lifetime.json"), lifetime);
    }
}
