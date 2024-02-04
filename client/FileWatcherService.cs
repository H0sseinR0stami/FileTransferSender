
using System.Collections.Concurrent;

namespace client;
public class FileWatcherService
{
    private static readonly LogData LogData = new LogData();
    private static readonly Configuration Config = new Configuration("../../../config.txt");
    private readonly string _ipAddress = Config.GetOsDependentIp();
    private readonly int _fileTransferPort = Config.GetIntValue("fileTransferPort");
    private readonly int _maxDeleteRetries = Config.GetIntValue("maxRetries");
    private readonly int _delayMillisecondsBetweenDeleteRetries = Config.GetIntValue("delayMillisecondsBetweenRetries");
    private readonly int _retryDelay = Config.GetIntValue("delay");
    private readonly string _watchFolder;
    private readonly ConcurrentQueue<string> _fileQueue = new ConcurrentQueue<string>();

    private readonly FolderMonitor _folderMonitor; // Assuming FolderMonitor is defined elsewhere
    private FileSystemWatcher? _watcher;
    
    private readonly Task _processingTask;
    private bool _isProcessing;
    
    public FileWatcherService(string watchFolder, long sizeLimitBytes)
    {
        _watchFolder = watchFolder;
        _folderMonitor = new FolderMonitor(_watchFolder, sizeLimitBytes);
        _isProcessing = true;
        _processingTask = Task.Run(() => ProcessFileQueueAsync());

        InitializeWatcher();
    }
    
    private void InitializeWatcher()
    {
        _watcher = new FileSystemWatcher(_watchFolder)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*"
        };

        _watcher.Created += (_, e) =>
        {
            if (File.Exists(e.FullPath) || Directory.Exists(e.FullPath))
            {
                _fileQueue.Enqueue(e.FullPath);
            }
        };
    }

    public void StartWatching()
    {
        if (_watcher != null) _watcher.EnableRaisingEvents = true;
    }

    private void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
        }
    }

    private void RestartWatching()
    {
        StopWatching();
        InitializeWatcher(); // Reinitialize the watcher
        StartWatching();
        LogData.Log($"Restarted watching folder: \"{_watchFolder}\"");
    }
    private async Task ProcessFileQueueAsync()
    {
        while (_isProcessing)
        {
            if (_fileQueue.TryDequeue(out var filePath))
            {
                if (Directory.Exists(filePath))
                {
                    await HandleDirectoryAsync(filePath, this);
                }
                else if (File.Exists(filePath))
                {
                    await HandleFileAsync(filePath, this);
                }
            }
            await Task.Delay(_retryDelay);
        }
    }


    private async Task HandleDirectoryAsync(string directoryPath,FileWatcherService fileWatcherService)
    {
        LogData.Log($"A directory was added: \"{directoryPath}\". Attempting to delete.");
        var deleted = await _folderMonitor.TryDeleteFolder(directoryPath, _maxDeleteRetries, _delayMillisecondsBetweenDeleteRetries); 
        if (deleted)
        {
            LogData.Log($"Directory \"{directoryPath}\" deleted successfully.");
            fileWatcherService.RestartWatching();
        }
        else
        {
            LogData.Log($"Failed to delete directory \"{directoryPath}\" after multiple attempts.");
            fileWatcherService.RestartWatching();
        }
    }

    private async Task HandleFileAsync(string filePath,FileWatcherService fileWatcherService)
    {
        LogData.Log($"New file detected: \'{filePath}\". Checking size constraints.");
        if (_folderMonitor.IsOverLimit())
        {
            LogData.Log($"Error: Folder size limit exceeded with file '{filePath}'. Deleting file.");
            var deleted = await _folderMonitor.TryDeleteFile(filePath, _maxDeleteRetries);
            if (deleted)
            {
                LogData.Log($"File \"{filePath}\" deleted successfully.");
                fileWatcherService.RestartWatching();
            }
            else
            {
                LogData.Log($"Failed to delete file \"{filePath}\" after multiple attempts.");
            }
        }
        else
        {
            LogData.Log($"File is within size limit: \"{filePath}\". Preparing to send.");
                try
                {
                    var client = new SentFile(_ipAddress, _fileTransferPort, filePath);
                    var isSuccess = await client.InitialSendAsync(); // Use localhost for testing
                    if (isSuccess)
                    {
                        LogData.Log($"File \"{filePath}\" sent successfully");
                        fileWatcherService.RestartWatching();
                    }
                    else
                    {
                        LogData.Log($"Sending \"{filePath}\" failed.");
                    }
                }
                catch (Exception ex)
                {
                    LogData.Log($"Error sending file: {ex.Message}");
                }
        }
    }

}
