
namespace client;
public class FolderMonitor
{
    private static readonly LogData LogData = new LogData();
    private static readonly Configuration Config = new Configuration("../../../config.txt");
    private readonly int _initialDelayMilliseconds = Config.GetIntValue("delayMillisecondsBetweenRetries");
    private readonly string _folderPath;
    private readonly long _sizeLimitBytes;
    
    

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
    
    public async Task<bool> TryDeleteFile(string filePath, int maxRetries)
    {
        var retryCount = 0;
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
                    LogData.Log($"Maximum retry attempts reached. Aborting operation.");
                    break;
                }

                int delay = _initialDelayMilliseconds * (int)Math.Pow(2, retryCount - 1);
                LogData.Log(
                    $"Error occurred: {ex.Message}. Retrying in {delay / 1000} seconds...");
                await Task.Delay(delay);
            }
        }
        return false; // File could not be deleted after retries
    }

    public async Task<bool> TryDeleteFolder(string folderPath, int maxRetries, int delayMillisecondsBetweenRetries)
    {
        var retryCount = 0;
        // Initial delay before the first deletion attempt
        await Task.Delay(delayMillisecondsBetweenRetries);
        while (Directory.Exists(folderPath))
        {
            try
            {
                Directory.Delete(folderPath, true); // true for recursive deletion
            }
            catch (IOException ex)
            {
                retryCount++;
                if (retryCount > maxRetries)
                {
                    LogData.Log($"Maximum retry attempts reached deleting folder \"{folderPath}\". Aborting operation.");
                    break;
                }

                int delay = _initialDelayMilliseconds * (int)Math.Pow(2, retryCount - 1);
                LogData.Log(
                    $"Attempt number {retryCount}: error occurred deleting folder \"{folderPath}\": {ex.Message}. Retrying in {delay / 1000} seconds...");
                await Task.Delay(delay);
            }
        }

        return !Directory.Exists(folderPath);
        
    }
}