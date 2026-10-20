using System;
using System.Collections.Generic;

public abstract class Ordinalizer { public abstract string Format(int value); }
public class EnglishOrdinalizer : Ordinalizer { public override string Format(int value) => $"{value}th"; }
public class FrenchOrdinalizer : Ordinalizer { public override string Format(int value) => value == 1 ? "1er" : $"{value}e"; }
public sealed class LocaleRegistry
{
    private readonly Dictionary<string, Ordinalizer> _locales = new(StringComparer.OrdinalIgnoreCase);
    public Ordinalizer Resolve(string locale) => _locales[locale];
}