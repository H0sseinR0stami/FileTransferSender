
using System.Net.Sockets;
using System.Text;

namespace client;
public class SentFile
{
    private static readonly LogData LogData = new LogData();
    private static readonly Configuration Config = new Configuration("../../../config.txt");
    private readonly int _delayMillisecondsBetweenRetries = Config.GetIntValue("delayMillisecondsBetweenRetries");
    private readonly int _maxRetries = Config.GetIntValue("maxRetries");
    private readonly int _timeoutMilliseconds = Config.GetIntValue("timeoutMilliseconds"); 
    private readonly int _pingPort = Config.GetIntValue("pingPort"); 
    private readonly int _delay = Config.GetIntValue("delay");
    private readonly int _fileBufferSize = Config.GetIntValue("fileBufferSize");
    private readonly int _pingBufferSize = Config.GetIntValue("pingBufferSize");
    private readonly string _ipAddress;
    private readonly int _fileTransferPort;
    private readonly string _filePath;
    private long _fileSize;
    private bool _fileSent;
    private TcpClient _client = null!;
    private NetworkStream _networkStream = null!;
    private CancellationTokenSource _pingTokenSource = null!;
    private TcpClient _pingClient = null!;
    

    public SentFile(string host, int port, string filePath)
    {
        _ipAddress = host;
        _fileTransferPort = port;
        _filePath = filePath;
    }

    public async Task<bool> InitialSendAsync()
    {
        LogData.Log($"Start sending \"{_filePath}\"");
        try
        {
            // get file size
            var fileInfo = new FileInfo(_filePath);
            _fileSize = fileInfo.Length;
        }
        catch (Exception ex)
        {
            LogData.Log($"Error getting file info: {ex.Message}");
            return false;
        }

        var retryCount = 0;
        while (!_fileSent && (retryCount <= _maxRetries))
        {
            try
            {
                // connecting for file transfer
                _client = new TcpClient();
                await _client.ConnectAsync(_ipAddress, _fileTransferPort);
                _networkStream = _client.GetStream();
                
                // connecting for checking the connection status
                _pingClient = new TcpClient();
                await _pingClient.ConnectAsync(_ipAddress, _pingPort);
                _pingTokenSource = new CancellationTokenSource();
                
                // start pinging and check for connection status
                StartPing();
                LogData.Log($"Successfully connected to server with IpAddress: \"{_ipAddress}\" " +
                            $", fileTransfer ports: \"{_fileTransferPort}\" and ping Port: \"{_pingPort}\"");
                
                // send file data
                await SendFileData();
            }
            catch (Exception ex)
            {
                LogData.Log($"Error in connecting to server: {ex.Message}. Retrying number {retryCount}");
                retryCount++;
                await Task.Delay(_delayMillisecondsBetweenRetries);
            }
            finally
            {
                StopPing();
                CloseConnections();
            }
        }

        return _fileSent;
    }

    private async Task SendFileData()
{
    try
    {
        // Open the file stream
        await using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read);

        // Extract file name and size
        var fileName = Path.GetFileName(_filePath);
        _fileSize = fileStream.Length;

        // Prepare the metadata message
        var metadata = $"FileName:{fileName};Size:{_fileSize};\n";
        var metadataBytes = Encoding.UTF8.GetBytes(metadata);

        // Send the metadata
        await _networkStream.WriteAsync(metadataBytes, 0, metadataBytes.Length);
        await _networkStream.FlushAsync();

        // Wait a bit before sending the file content
        await Task.Delay(_delay);
    
        LogData.Log($"Sent request for file: \"{fileName}\", size: \"{_fileSize}\" bytes");
    
        var serverFileSizeBytes = new byte[8];
        var read = _networkStream.Read(serverFileSizeBytes, 0, 8);
        if (read == 8)
        {
            var serverFileSize = BitConverter.ToInt64(serverFileSizeBytes, 0);
            if (serverFileSize == _fileSize)
            {
                LogData.Log("File already exists on the server with the same size.");
            }
            else
            {
                fileStream.Seek(serverFileSize, SeekOrigin.Begin);
   
                LogData.Log($"Resuming file transfer from byte {serverFileSize}");
                var buffer = new byte[_fileBufferSize];
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
            
                    await _networkStream.WriteAsync(buffer, 0, bytesRead);
                    await _networkStream.FlushAsync();
            
                    //Log($" new chunk sent. Sent {serverFileSize+totalSent} of {_fileSize} bytes");
                    if (!_client.Connected)
                    {
                        throw new InvalidOperationException("Connection lost. Attempting to reconnect...");
                    }
                }
            }
        }
        else
        {
            throw new Exception($"serverFileSizeBytes is expected to be 8 bytes but is {read} bytes");
        }
        _fileSent = true;
        LogData.Log($"File \"{fileName}\" sent successfully.");
    } 
    catch (Exception ex)
    {
        LogData.Log($"Error sending file: {ex.Message}");
        await Task.Delay(_delayMillisecondsBetweenRetries);
    }
    
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

    private void StartPing()
    {
        LogData.Log("Ping-Pong Started!");
        Task.Run(async () =>
        {
            var pingStream = _pingClient.GetStream();
            var pingMessage = Encoding.UTF8.GetBytes("ping\n");
            var responseBuffer = new byte[_pingBufferSize];

            while (!_pingTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (pingStream.CanWrite)
                    {
                        await pingStream.WriteAsync(pingMessage, 0, pingMessage.Length);
                        LogData.Log("Ping sent to server.");

                        string response = await ReadWithTimeout(pingStream, responseBuffer, _timeoutMilliseconds);
                        if (response.Trim() != "pong")
                        {
                            throw new Exception("Pong not received.");
                        }
                        LogData.Log("Pong received from server.");
                    }
                    else
                    {
                        // Handle the case where the stream cannot be written (disposed)
                        LogData.Log("Cannot write to the ping stream. Attempting to reconnect...");
                        await ReconnectAsync();
                    }
                }
                catch (TimeoutException)
                {
                    LogData.Log("Pong reception timed out. Attempting to reconnect...");
                    await ReconnectAsync();
                }
                catch (Exception ex)
                {
                    LogData.Log($"Error in pinging: {ex.Message}. Attempting to reconnect...");
                    await ReconnectAsync();
                }

                await Task.Delay(_delay);
            }
        }, _pingTokenSource.Token);
    }

    private async Task ReconnectAsync()
    {
        for (var retry = 0; retry <= _maxRetries; retry++)
        {
            try
            {
                CloseConnections(); // Close previous connections if any

                await ReconnectClientAsync(_client, _ipAddress, _fileTransferPort);
                LogData.Log("Reconnected to the file transfer server.");

                await ReconnectClientAsync(_pingClient, _ipAddress, _pingPort);
                StartPing();
                LogData.Log("Reconnected to the ping server.");

                return; // Successful connection, exit the method
            }
            catch (Exception ex)
            {
                LogData.Log($"Attempt {retry + 1} failed to reconnect: {ex.Message}");
                StopPing();
            }

            if (retry <= _maxRetries)
            {
                LogData.Log($"Waiting {_delayMillisecondsBetweenRetries} Milliseconds before next attempt...");
                await Task.Delay(_delayMillisecondsBetweenRetries);
            }
        }
        LogData.Log("All reconnect attempts failed.");
    }

    private void CloseConnections()
    {
        _networkStream?.Close();
        _client?.Close();
        _pingClient?.Close();
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
    
    private void StopPing()
    {
        _pingTokenSource?.Cancel();
        LogData.Log("ping stopped!");
    }

}
