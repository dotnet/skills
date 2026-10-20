using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

internal static class DiagnosticTimeoutContract
{
    [ModuleInitializer]
    internal static void Verify()
    {
        var assembly = typeof(DiagnosticTimeoutContract).Assembly;
        var types = assembly.GetTypes().Where(type => type.IsDefined(typeof(TestClassAttribute)));
        foreach (var type in types)
        {
            if (type.GetCustomAttributes(inherit: true).Any(attribute => attribute is TimeoutAttribute) ||
                type.GetMethods().Any(method => method.IsDefined(typeof(TimeoutAttribute), inherit: true)))
            {
                throw new InvalidOperationException(
                    "Diagnostic timeout contract: warning converted to an enforced timeout.");
            }
        }
    }
}
