using OpenFTTH.NotificationClient;
using System.Net;
using System.Text.Json;

namespace Example;

internal sealed record Customer
{
    public string Name { get; init; }
    public int Age { get; init; }

    public Customer(string name, int age)
    {
        Name = name;
        Age = age;
    }
}

internal static class Program
{
    public static void Main()
    {
        var address = IPAddress.Parse("127.0.0.1");
        var port = 8000;

        var client = new Client(address, port);
        var connection = client.Connect();

        _ = Task.Run(async () =>
        {
            Console.WriteLine("Starting reading...");
            await foreach (var x in connection.ReadAllAsync())
            {
                if (x.Type == nameof(Customer))
                {
                    var c = JsonSerializer.Deserialize<Customer>(x.Body)
                        ?? throw new InvalidOperationException(
                            $"Could not deserialize {nameof(Customer)}.");

                    Console.WriteLine(
                        $"Received customer with: name :{c.Name} and age: {c.Age}.");
                }
            }
        });

        for (var i = 0; i < 100000; i++)
        {
            var c = JsonSerializer.Serialize(new Customer("Rune", i));
            client.Send(new(nameof(Customer), c));
        }

        Console.ReadLine();
        client.Disconnect();
    }
}
