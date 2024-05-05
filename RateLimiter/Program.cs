using RateLimiter;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Func<int, Task> action = async (input) =>
        {
            Console.WriteLine($"Action called with input: {input} at {DateTime.UtcNow}");
            await Task.Delay(1000);  //Simulate some work
        };

        var rateLimiter = new RateLimiter<int>(action,
            (TimeSpan.FromSeconds(10), 2),  //Limit to 2 calls per 10 seconds
            (TimeSpan.FromMinutes(1), 5)); //Limit to 5 calls per minute

        //Perform actions
        for (int i = 0; i < 10; i++)
        {
            await rateLimiter.Perform(i);
        }
    }
}
