using System.Runtime.InteropServices;

namespace client;

public class Configuration
{
    private Dictionary<string, string> _configValues = new Dictionary<string, string>();

    public Configuration(string filePath)
    {
        LoadConfiguration(filePath);
    }

    private void LoadConfiguration(string filePath)
    {
        foreach (var line in File.ReadAllLines(filePath))
        {
            var keyValue = line.Split('=');
            if (keyValue.Length == 2)
            {
                _configValues[keyValue[0].Trim()] = keyValue[1].Trim();
            }
        }
    }

    public string GetStringValue(string key)
    {
        if (_configValues.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException($"Key not found: {key}");
    }

    public int GetIntValue(string key)
    {
        if (_configValues.TryGetValue(key, out var value))
        {
            if (int.TryParse(value, out int intValue))
            {
                return intValue;
            }
            throw new FormatException($"Value for {key} is not a valid integer.");
        }

        throw new KeyNotFoundException($"Key not found: {key}");
    }

    public long GetLongValue(string key)
    {
        if (_configValues.TryGetValue(key, out var value))
        {
            if (long.TryParse(value, out long longValue))
            {
                return longValue;
            }
            throw new FormatException($"Value for {key} is not a valid long integer.");
        }

        throw new KeyNotFoundException($"Key not found: {key}");
    }
    
    public string GetOsDependentPath()
    {
        string key = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windowsPath" : "linuxPath";
        return GetStringValue(key);
    }
    
    public string GetOsDependentIp()
    {
        string key = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windowsIp" : "linuxIp";
        return GetStringValue(key);
    }
    
}