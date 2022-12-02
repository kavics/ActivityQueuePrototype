using ActivityQueuePrototype;
using SenseNet.Diagnostics;

SnTrace.EnableAll();
SnTrace.SnTracers.Add(new SnFileSystemTracer());
SnTrace.Write("--------------------------------------");

using var app = new App(args);
await app.RunAsync();