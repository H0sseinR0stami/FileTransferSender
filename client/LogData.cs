namespace client;

public class LogData
{
    private static readonly Configuration Config = new Configuration("../../../config.txt");
    private readonly string _logFilePath = Config.GetStringValue("logFilePath");
    
    public void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now}] {message}");
        
        try
        {
            // AppendAllText will create the file if it does not exist
            File.AppendAllText(_logFilePath, $"{DateTime.Now}: {message}\n");
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"I/O Error while writing to log file: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Console.WriteLine($"Access Error while writing to log file: {uaEx.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while writing to log file: {ex.Message}");
        }
    }
}