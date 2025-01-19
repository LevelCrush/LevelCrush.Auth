using System.Xml;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;

namespace auth_server;

public static class LoggerGlobal
{
    private static  ILoggerFactory _factory;
    private static  ILogger _default;
    public static string Name { get;  set; } = "default";
    static LoggerGlobal()
    {
        _factory = LoggerFactory.Create(builder => builder
            .AddConsole()
            .AddFile("app_{1}_{0:yyyy}-{0:MM}-{0:dd}.log", options =>
            {
                options.Append = true;
                options.FormatLogFileName = fName =>
                {
                    return String.Format(fName, DateTime.Now, Name);
                };
            } )
            .SetMinimumLevel(LogLevel.Information));
        _default = _factory.CreateLogger("Global");
    }
    
    public static ILogger Create(string name) 
    {
        return _factory.CreateLogger(name);
    }

    public static ILogger Raw()
    {
        return _default;
    }
    
    public static void Close()
    {
        _factory.Dispose();
    }

    public static void Write(string message, LogLevel level = LogLevel.Information)
    {
        _default.Log(level, message);
    }
}