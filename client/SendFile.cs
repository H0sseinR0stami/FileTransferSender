
using System.Net.Sockets;
using System.Text;

namespace client;
public class SentFile
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _filePath;
    private const int RetryDelayMilliseconds = 2000; // Constant delay between retries
    private long _fileSize;
    private const int MaxRetries = 1000;
    private int _retryCount;
    private bool _fileSent;
    private TcpClient _client;
    private NetworkStream _networkStream;
    private CancellationTokenSource _heartbeatTokenSource;
    private readonly int _heartbeatInterval = 500; // 0.5 seconds
    private TcpClient _heartbeatClient;
    private readonly int _heartbeatPort = 1235; // Separate port for heartbeat

    public SentFile(string host, int port, string filePath)
    {
        _host = host;
        _port = port;
        _filePath = filePath;
    }

    public async Task<bool> SendAsync()
    {
        try
        {
            var fileInfo = new FileInfo(_filePath);
            _fileSize = fileInfo.Length;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file info: {ex.Message}");
            return false;
        }

        while (!_fileSent && _retryCount < MaxRetries)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_host, _port);
                _networkStream = _client.GetStream();

                _heartbeatClient = new TcpClient();
                await _heartbeatClient.ConnectAsync(_host, _heartbeatPort);
                _heartbeatTokenSource = new CancellationTokenSource();
                StartHeartbeat();
                Console.WriteLine($"[{DateTime.Now}] Connected to server. Sending file: {_filePath}");
                await SendFileData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] Error: {ex.Message}. Retrying...");
                _retryCount++;
                await Task.Delay(RetryDelayMilliseconds);
            }
            finally
            {
                StopHeartbeat();
                _networkStream?.Close();
                _client?.Close();
                _heartbeatClient?.Close();
            }
        }

        return _fileSent;
    }

    private async Task SendFileData()
{
    // Open the file stream
    await using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);

    // Extract file name and size
    string fileName = Path.GetFileName(_filePath);
    _fileSize = fileStream.Length;

    // Prepare the metadata message
    string metadata = $"FileName:{fileName};Size:{_fileSize};\n";
    byte[] metadataBytes = Encoding.UTF8.GetBytes(metadata);

    // Send the metadata
    await _networkStream.WriteAsync(metadataBytes, 0, metadataBytes.Length);
    await _networkStream.FlushAsync();

    // Wait a bit before sending the file content
    await Task.Delay(100);
    
    Log($"Sent request for file: {fileName}, size: {_fileSize} bytes");
    
    byte[] serverFileSizeBytes = new byte[8];
    _networkStream.Read(serverFileSizeBytes, 0, 8);
    long serverFileSize = BitConverter.ToInt64(serverFileSizeBytes, 0);
    if (serverFileSize == _fileSize)
    {
        Log("File already exists on the server with the same size.");
    }
    else
    {
        fileStream.Seek(serverFileSize, SeekOrigin.Begin);
   
        Log($"Resuming file transfer from byte {serverFileSize}");
        int bufferSize = 4096;
        var buffer = new byte[bufferSize];
        int bytesRead;
        long totalSent = 0;

        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            
            await _networkStream.WriteAsync(buffer, 0, bytesRead);
            totalSent += bytesRead;
            await _networkStream.FlushAsync();
            
            //Console.WriteLine($"[{DateTime.Now}] new chunk sent. Sent {serverFileSize+totalSent} of {_fileSize} bytes");
            if (!_client.Connected)
            {
                throw new InvalidOperationException("Connection lost. Attempting to reconnect...");
            }
        }
    }

    _fileSent = true;
    Log("File sent.");
}


    private async Task<string> ReadWithTimeout(NetworkStream stream, byte[] buffer, int timeoutMilliseconds)
    {
        var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
        if (await Task.WhenAny(readTask, Task.Delay(timeoutMilliseconds)) == readTask)
        {
            // Read completed within timeout
            return Encoding.UTF8.GetString(buffer, 0, readTask.Result).Trim();
        }
        else
        {
            // Timeout
            throw new TimeoutException("Read operation timed out.");
        }
    }

    private void StartHeartbeat()
    {
        Console.WriteLine("Ping-Pong Heartbeat Started!");
        Task.Run(async () =>
        {
            var heartbeatStream = _heartbeatClient.GetStream();
            var heartbeatMessage = Encoding.UTF8.GetBytes("ping\n");
            var responseBuffer = new byte[1024];
            int pongTimeoutMilliseconds = 100; // 0.5 second timeout for pong response

            while (!_heartbeatTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await heartbeatStream.WriteAsync(heartbeatMessage, 0, heartbeatMessage.Length);
                    Console.WriteLine("Ping sent to server.");

                    string response = await ReadWithTimeout(heartbeatStream, responseBuffer, pongTimeoutMilliseconds);
                    if (response.Trim() != "pong")
                    {
                        throw new Exception("Pong not received.");
                    }
                    Console.WriteLine("Pong received from server.");
                }
                catch (TimeoutException)
                {
                    Console.WriteLine("Pong reception timed out. Attempting to reconnect...");
                    await ReconnectAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in heartbeat: {ex.Message}. Attempting to reconnect...");
                    await ReconnectAsync();
                }

                await Task.Delay(_heartbeatInterval);
            }
        }, _heartbeatTokenSource.Token);
    }

    private async Task ReconnectAsync()
    {
        int maxRetries = 1000;
        int delayBetweenRetriesInSeconds = 1;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                CloseConnections(); // Close previous connections if any

                await ReconnectClientAsync(_client, _host, _port);
                Console.WriteLine("Reconnected to the file transfer server.");

                await ReconnectClientAsync(_heartbeatClient, _host, _heartbeatPort);
                StartHeartbeat();
                Console.WriteLine("Reconnected to the heartbeat server.");

                return; // Successful connection, exit the method
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {retry + 1} failed to reconnect: {ex.Message}");
                StopHeartbeat();
            }

            if (retry < maxRetries - 1)
            {
                Console.WriteLine($"Waiting {delayBetweenRetriesInSeconds} seconds before next attempt...");
                await Task.Delay(TimeSpan.FromSeconds(delayBetweenRetriesInSeconds));
            }
        }

        Console.WriteLine("All reconnect attempts failed.");
    }

    private void CloseConnections()
    {
        _networkStream?.Close();
        _client?.Close();
        _heartbeatClient?.Close();
    }

    private async Task ReconnectClientAsync(TcpClient client, string host, int port)
    {
        client.Close(); // Ensure any previous client is closed
        client = new TcpClient();

        await client.ConnectAsync(host, port);
        if (client == _client)
        {
            _networkStream = client.GetStream();
        }
    }



    private void StopHeartbeat()
    {
        _heartbeatTokenSource?.Cancel();
        Console.WriteLine("Heartbeat stopped!");
    }
    private static void LogToFile(string message)
    {
        string logFilePath = "file_transfer_log.txt";

        try
        {
            // AppendAllText will create the file if it does not exist
            File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
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
    
    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now}] {message}");
        // Additional logging to file can be implemented here.
    }
}
