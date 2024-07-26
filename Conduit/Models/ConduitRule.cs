namespace Conduit.Models;

public class ConduitRule
{
    public ConduitVerb Verb { get; set; }
    public string Data { get; set; }
}

public enum ConduitVerb
{
    ALLOW,
    DENY,
    FORWARD,
    DEFINE,
}