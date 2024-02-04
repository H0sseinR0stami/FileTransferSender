
namespace client;
public class FolderMonitor
{
    private readonly string _folderPath;
    private readonly long _sizeLimitBytes;
    private const int initialDelayMilliseconds = 4000; // milliseconds
    int retryCount = 0;
    

    public FolderMonitor(string folderPath, long sizeLimitBytes)
    {
        _folderPath = folderPath;
        _sizeLimitBytes = sizeLimitBytes;
    }

    private long GetFolderSize()
    {
        return new DirectoryInfo(_folderPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }

    public bool IsOverLimit()
    {
        var currentSize = GetFolderSize();
        return currentSize > _sizeLimitBytes;
    }
    
    public async Task<bool> TryDeleteFile(string filePath, int maxRetries, int delayMilliseconds)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true; // File deleted successfully
                }
            }
            catch (IOException ex)
            {
                retryCount++;
                if (retryCount > maxRetries)
                {
                    Console.WriteLine($"[{DateTime.Now}] Maximum retry attempts reached. Aborting operation.");
                    break;
                }

                int delay = initialDelayMilliseconds * (int)Math.Pow(2, retryCount - 1);
                Console.WriteLine(
                    $"[{DateTime.Now}] Error occurred: {ex.Message}. Retrying in {delay / 1000} seconds...");
                await Task.Delay(delay);
                await Task.Delay(delayMilliseconds);
            }
        }
        return false; // File could not be deleted after retries
    }

    public async Task<bool> TryDeleteFolder(string folderPath, int maxRetries, int initialDelayMilliseconds, int delayMillisecondsBetweenRetries)
    {
        // Initial delay before the first deletion attempt
        await Task.Delay(initialDelayMilliseconds);

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true); // true for recursive deletion
                    return true; // Folder deleted successfully
                }
                return false; // Folder no longer exists
            }
            catch (IOException)
            {
                await Task.Delay(delayMillisecondsBetweenRetries);
            }
        }
        return false; // Folder could not be deleted after retries
    }
}