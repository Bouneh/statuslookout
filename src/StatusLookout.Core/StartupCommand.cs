namespace StatusLookout.Core;

public static class StartupCommand
{
    public static string Create(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executable.Any(c => c == '"' || char.IsControl(c)))
            throw new InvalidDataException("Windows startup requires an absolute executable path.");
        var command = $"\"{executable}\" --background";
        if (command.Length > 260) throw new InvalidDataException("The executable path is too long for Windows startup.");
        return command;
    }
    public static bool Matches(string? registered, string executable) =>
        string.Equals(registered?.Trim(), Create(executable), StringComparison.OrdinalIgnoreCase);
    public static bool NeedsRepair(string? registered, string executable) =>
        !string.IsNullOrWhiteSpace(registered) && !Matches(registered, executable);
}
