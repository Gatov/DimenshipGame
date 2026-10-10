using Dimenship.Core.Content;
using Dimenship.Replay;

// dotnet run --project tools/Dimenship.Replay -- <content-root> <script.json> [policy]
//
// The content root is an argument rather than a fixed path so the same script can be run against
// the tree before a change and after it, which is what every later scheduling ticket reports. The
// policy is one of Policies.Names, and queue order when left out (E2).

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: Dimenship.Replay <content-root> <script.json> [policy]");
    Console.Error.WriteLine("policies: " + string.Join(", ", Policies.Names));
    return 2;
}

var policy = args.Length == 3 ? args[2] : Policies.Names[0];
if (Policies.Create(policy) is null)
{
    Console.Error.WriteLine($"unknown policy '{policy}'; policies: " + string.Join(", ", Policies.Names));
    return 2;
}

var content = new JsonContentSource(new DirectoryContentFileSystem(args[0])).Load();
if (!content.Succeeded)
{
    foreach (var error in content.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var parsed = ReplayScript.Parse(File.ReadAllText(args[1]), content.Catalog!, content.Scenarios);
if (!parsed.Succeeded)
{
    foreach (var error in parsed.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var script = parsed.Script!;
var scenario = content.Scenarios.Single(s => s.Id == script.Scenario);
Console.Out.Write(ReplayReport.Format(Replay.Run(content.Catalog!, scenario, script, Policies.Create(policy))));
return 0;
