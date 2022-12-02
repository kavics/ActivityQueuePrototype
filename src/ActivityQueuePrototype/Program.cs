using ActivityQueuePrototype;
using SenseNet.Diagnostics;

//for (int i = 0; i < 20; i++)
//{
//    foreach(var id in new ActivityGenerator().GenerateIds(20, 5))
//        Console.Write(" {0}", id);
//    Console.WriteLine();
//}
//return;

//for (int i = 0; i < 20; i++)
//{
//    foreach (var activity in new ActivityGenerator().Generate(20,
//                 5, new RngConfig(1, 10), new RngConfig(10, 200)))
//        Console.Write(" {0}", activity.Id);
//    Console.WriteLine();
//}
//return;

SnTrace.EnableAll();
SnTrace.SnTracers.Add(new SnFileSystemTracer());
SnTrace.Write("-------------------------------------->");

using (var app = new App(args))
    await app.RunAsync();

SnTrace.Write("<--------------------------------------");
