namespace Machines;

public static class MachineLabels
{
    public static string Create(string service) =>
        $"{service}@{Environment.MachineName}";
}
