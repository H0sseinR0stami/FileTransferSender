
namespace client;

internal static class Program
{
    private static readonly LogData LogData = new LogData();
    private static void Main(string[] args)
    {
        // reading configurations
        var config = new Configuration("../../../config.txt");

        try
        {
            var osDependentPath = config.GetOsDependentPath();
            
            var watchFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, osDependentPath);
            if (!Directory.Exists(watchFolder))
            {
                Directory.CreateDirectory(watchFolder);
                LogData.Log($"Send folder created at: \"{watchFolder}\"");
            }

            var sizeLimitBytes = config.GetLongValue("sizeLimitBytes");
            LogData.Log($"\"{watchFolder}\" sizeLimitBytes is \"{sizeLimitBytes}\"");
            var folderMonitor = new FileWatcherService(watchFolder, sizeLimitBytes);
            folderMonitor.StartWatching();

 
            LogData.Log($"start watching folder: \"{osDependentPath}\"");
            Console.ReadLine(); // Keep the application running
            
        }
        catch (Exception ex)
        {
            LogData.Log($"Error in creating or monitoring send folder: {ex.Message}");
        }
    }
}