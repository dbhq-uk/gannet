using Xunit;

namespace Gannet.TestSupport;

/// <summary>A fact that runs only when a file exists under a special folder (for example the home directory). Otherwise it is reported as skipped, never as passed.</summary>
public sealed class FileFactAttribute : FactAttribute
{
    public FileFactAttribute(Environment.SpecialFolder folder, string relativePath)
    {
        var path = Path.Combine(Environment.GetFolderPath(folder), relativePath);
        if (!File.Exists(path))
        {
            Skip = $"The file {relativePath} is not on this machine.";
        }
    }
}
