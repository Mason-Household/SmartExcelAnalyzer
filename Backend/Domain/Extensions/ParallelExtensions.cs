namespace Domain.Extensions;

public static class ParallelExtensions
{
    public static async Task ForEachAsync<T>(
        this IEnumerable<T> source,
        CancellationToken cancellationToken,
        Func<T, CancellationToken, Task> body
    )
    {
        await Task.WhenAll(
            source.Select(item => 
                Task.Run(
                    () => 
                    body(item, 
                         cancellationToken
                    ), 
                    cancellationToken
                )
            )
        );
    }
}