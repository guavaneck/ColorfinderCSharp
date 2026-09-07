using System.Diagnostics;
using ColorFinder.Desktop.Platform;

if (args.Length == 0) throw new ArgumentException("Pass a code file to open.");
var thresholdMilliseconds = args.Length > 1 ? double.Parse(args[1]) : 50;
var timer = Stopwatch.StartNew();
new PlatformService().Open(args[0]);
timer.Stop();
Console.WriteLine($"Open handoff: {timer.Elapsed.TotalMilliseconds:F2} ms (threshold: {thresholdMilliseconds:F2} ms)");
return timer.Elapsed.TotalMilliseconds < thresholdMilliseconds ? 0 : 1;
