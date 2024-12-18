using Reown.Core.Common.Logging;
using Xunit.Abstractions;

namespace Reown.TestUtils;

public class TestOutputHelperLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    
    public TestOutputHelperLogger(ITestOutputHelper output)
    {
        _output = output;
    }
    
    public void Log(string message)
    {
        _output.WriteLine(message);
    }

    public void LogError(string message)
    {
        _output.WriteLine(message);
    }

    public void LogError(Exception e)
    {
        _output.WriteLine(e.ToString());
    }
}