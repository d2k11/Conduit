using System.Runtime.InteropServices;
using Conduit.Models;

namespace Conduit;

public static class RuleManager
{
    public static List<ConduitRule> Rules { get; set; } = new();

    public static void Load()
    {
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        string filePath = isLinux ? "/root/Conduit/conduit.conf" : "conduit.conf";
        if (!File.Exists(filePath))
        {
            File.Create(filePath).Close();
            File.WriteAllText(filePath, "# Conduit Configuration File #\n\nDEFINE localhost AS 127.0.0.1\n\nFORWARD 8888 TO localhost:22");
        }   
        
        List<string> lines = File.ReadAllLines(filePath).ToList();
        foreach (string line in lines)
        {
            if (line.StartsWith("DEFINE"))
            {
                string initial = line.Split("AS")[0].Split(' ')[1].Trim();
                string post = line.Split("AS")[1].Trim();
                Rules.Add(new ConduitRule { Verb = ConduitVerb.DEFINE, Data = $"{initial}|{post}" });
            }

            if (line.StartsWith("ALLOW"))
            {
                string newline = Replace(line);
                string target = newline.Split("TO")[1].Trim();
                string source = newline.Split("ALLOW")[1].Split("TO")[0].Trim();
                Rules.Add(new ConduitRule { Verb = ConduitVerb.ALLOW, Data = $"{source}|{target}" });
            }
            
            if (line.StartsWith("DENY"))
            {
                string newline = Replace(line);
                string target = newline.Split("TO")[1].Trim();
                string source = newline.Split("DENY")[1].Split("TO")[0].Trim();
                Rules.Add(new ConduitRule { Verb = ConduitVerb.DENY, Data = $"{source}|{target}" });
            }
            
            if (line.StartsWith("FORWARD"))
            {
                string newline = Replace(line);
                string target = newline.Split("TO")[1].Trim();
                string source = newline.Split("FORWARD")[1].Split("TO")[0].Trim();
                Rules.Add(new ConduitRule { Verb = ConduitVerb.FORWARD, Data = $"{source}|{target}" });
            }
        }
    }

    public static string Replace(string line)
    {
        foreach (ConduitRule rule in Rules)
        {
            if (rule.Verb == ConduitVerb.DEFINE)
            {
                string initial = rule.Data.Split('|')[0].Trim();
                string post = rule.Data.Split('|')[1].Trim();
                line = line.Replace(initial, post);
            }
        }

        return line;
    }

    public static string ReverseAlias(string ip)
    {
        foreach (ConduitRule rule in Rules)
        {
            if (rule.Verb == ConduitVerb.DEFINE)
            {
                string initial = rule.Data.Split('|')[0].Trim();
                string post = rule.Data.Split('|')[1].Trim();
                if(ip == post)
                {
                    return initial;
                }
            }
        }

        return "";
    }

    public static string GetAlias(string alias)
    {
        foreach (ConduitRule rule in Rules)
        {
            if (rule.Verb == ConduitVerb.DEFINE)
            {
                string initial = rule.Data.Split('|')[0].Trim();
                string post = rule.Data.Split('|')[1].Trim();
                if(alias == initial)
                {
                    return post;
                }
            }
        }

        return "";
    }

    public static bool Allow(string ip, int port)
    {
        foreach (ConduitRule rule in Rules)
        {
            if (rule.Verb == ConduitVerb.ALLOW)
            {
                string target = rule.Data.Split('|')[1];
                string source = rule.Data.Split('|')[0];
                if (source == ip && target == port.ToString())
                {
                    return true;
                }

                if (source == "ALL" && target == port.ToString())
                {
                    return true;
                }
                
                if(source == ip && target == "ALL")
                {
                    return true;
                }
            }
            
            if (rule.Verb == ConduitVerb.DENY)
            {
                string target = rule.Data.Split('|')[1];
                string source = rule.Data.Split('|')[0];
                if (source == ip && target == port.ToString())
                {
                    return false;
                }

                if (source == "ALL" && target == port.ToString())
                {
                    return false;
                }
                
                if(source == ip && target == "ALL")
                {
                    return false;
                }
            }
        }

        return false;
    }
}