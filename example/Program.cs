using OpenFTTH.NotificationClient;
using System.Net;
using System.Net.Sockets;
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
        var domain = "localhost";
        var port = 8000;

        var ipAddress = Dns.GetHostEntry(domain).AddressList
            .First(x => x.AddressFamily == AddressFamily.InterNetwork);

        using var client = new Client(ipAddress, port);
        var connectionCh = client.Connect();

        _ = Task.Run(async () =>
        {
            Console.WriteLine("Starting reading...");
            await foreach (var x in connectionCh.ReadAllAsync())
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

        for (var i = 0; i < 10; i++)
        {
            var c = JsonSerializer.Serialize(new Customer("Rune", i));
            client.Send(new(nameof(Customer), c));
        }

        Console.ReadLine();
    }
}
