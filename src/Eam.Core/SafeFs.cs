namespace Eam.Core;

/// <summary>
/// File system helpers that survive Windows' short locks. Right after many files are written,
/// the antivirus, the search indexer or OneDrive often hold a handle for a moment, and moving
/// the folder fails with "Access denied". We retry, then fall back to moving file by file.
/// </summary>
public static class SafeFs
{
    public static void MoveDirectory(string source, string destination)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                last = e;
                if (Directory.Exists(destination) && !Directory.Exists(source)) return;
                Thread.Sleep(250 * (attempt + 1));
            }
        }
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw last!;
        // Fallback: same volume moves of single files are rarely blocked.
        try
        {
            MoveContents(source, destination);
            DeleteDirectory(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not move '{source}' to '{destination}': {e.Message}", last);
        }
    }

    private static void MoveContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source))
            MoveContents(dir, Path.Combine(destination, Path.GetFileName(dir)));
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(destination, Path.GetFileName(file));
            Retry(() => File.Move(file, dest, overwrite: true));
        }
    }

    /// <summary>Deletes a folder, retrying a few times when Windows still holds a file.</summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
        Retry(() => { if (Directory.Exists(path)) Directory.Delete(path, true); });
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 6)
            { Thread.Sleep(250 * (attempt + 1)); }
        }
    }
}
