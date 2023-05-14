using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace HeelsPlugin;

public class Configuration
{
    public List<ConfigModel> Configs = new();
}

public class ConfigModel
{
    public string? Name;
    public uint ModelMain;
    public float Offset;
    public bool Enabled;
}
