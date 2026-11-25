public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var vm = new SettingsViewModel();
        var names = new List<string?>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        vm.Theme = "Dark"; vm.Theme = "Dark"; vm.Theme = "Light";
        Require(vm.Theme == "Light" && names.SequenceEqual(new[] { "Theme", "Theme" }),
            "bindings require the public property name and no notification on unchanged values");
        Console.WriteLine("PASS: behavior-contract");
    }
}
