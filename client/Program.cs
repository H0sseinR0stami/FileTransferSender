
namespace client;

internal static class Program
{
    private static void Main(string[] args)
    {
        // reading configurations
        var config = new Configuration("../../../config.txt");

        try
        {
            string osDependentPath = config.GetOsDependentPath();
            
            string watchFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, osDependentPath);
            if (!Directory.Exists(watchFolder))
            {
                Directory.CreateDirectory(watchFolder);
                Console.WriteLine($"Folder created at: {watchFolder}");
            }

            long sizeLimitBytes = config.GetLongValue("sizeLimitBytes");
            Console.WriteLine($"sizeLimitBytes is {sizeLimitBytes}");
            var folderMonitor = new FileWatcherService(watchFolder, sizeLimitBytes);
            folderMonitor.StartWatching();

 
            Console.WriteLine($"start watching folder: {osDependentPath}");
            Console.ReadLine(); // Keep the application running
            
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}