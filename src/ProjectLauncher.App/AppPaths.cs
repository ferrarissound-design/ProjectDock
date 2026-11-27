namespace ProjectLauncher.App;

internal static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectLauncher");

    public static string ProjectsFile => Path.Combine(DataDirectory, "projects.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
