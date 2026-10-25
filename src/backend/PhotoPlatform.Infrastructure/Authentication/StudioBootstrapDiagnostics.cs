using Npgsql;

namespace PhotoPlatform.Infrastructure.Authentication;

public static class StudioBootstrapDiagnostics
{
    private const int MaxNodes = 8;

    private const int MaxDepth = 8;

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var nodes = new List<string>(MaxNodes);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        Append(exception, nodes, seen, depth: 0);
        return string.Join(" -> ", nodes);
    }

    private static void Append(Exception exception, List<string> nodes, HashSet<Exception> seen, int depth)
    {
        if (nodes.Count >= MaxNodes || depth >= MaxDepth || !seen.Add(exception))
        {
            return;
        }

        nodes.Add(DescribeNode(exception));
        if (nodes.Count >= MaxNodes)
        {
            return;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Append(inner, nodes, seen, depth + 1);
                if (nodes.Count >= MaxNodes)
                {
                    return;
                }
            }

            return;
        }

        if (exception.InnerException is not null)
        {
            Append(exception.InnerException, nodes, seen, depth + 1);
        }
    }

    private static string DescribeNode(Exception exception)
    {
        if (exception is PostgresException postgres)
        {
            return typeof(PostgresException).FullName + " " + postgres.SqlState;
        }

        var name = exception.GetType().FullName ?? exception.GetType().Name;
        if (exception is InvalidOperationException
            && string.Equals(
                exception.Message,
                InfrastructureServiceCollectionExtensions.MissingConnectionStringMessage,
                StringComparison.Ordinal))
        {
            return name + " " + exception.Message;
        }

        return name;
    }
}
