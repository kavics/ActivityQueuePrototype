using SecurityActivityQueuePrototype;
using SenseNet.Diagnostics;




//Task parent = Task.Factory.StartNew(() =>
//{
//    Task child = Task.Factory.StartNew(() => Thread.Sleep(200),
//        TaskCreationOptions.AttachedToParent);
//});
//Console.WriteLine($"Status: {parent.Status}");
//Thread.Sleep(110);
//Console.WriteLine($"Status: {parent.Status}");
//Thread.Sleep(110);
//Console.WriteLine($"Status: {parent.Status}");
// /*
// result:
// Status: WaitingToRun
// Status: WaitingForChildrenToComplete
// Status: RanToCompletion
// */
//return;

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

//var generator = new ActivityGenerator();
//var maxId = 10;
//for (int iter = 0; iter < 20; iter++)
//{
//    var ids = generator.GenerateRandomIds(maxId).ToArray();
//    foreach (var id in ids)
//        Console.Write(" {0}", id);
//    for (int i = 1; i <= maxId; i++)
//        if (!ids.Contains(i))
//            Console.WriteLine("\nERROR: Missing id: " + i);
//    Console.WriteLine();
//}
//return;


SnTrace.EnableAll();
SnTrace.SnTracers.Add(new SnFileSystemTracer());
SnTrace.Write("-------------------------------------->");

using (var app = new App(args))
    await app.RunAsync();

SnTrace.Write("<--------------------------------------");
