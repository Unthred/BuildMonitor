namespace BuildMonitor.Infrastructure.LocalBuild;

public static class ExceptionDetailFormatter
{
    public static string Format(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return string.Join(
                Environment.NewLine,
                aggregate.Flatten().InnerExceptions.Select(Format));
        }

        var details = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : $"{exception.Message} ({exception.GetType().Name})";

        if (exception.InnerException is not null)
        {
            details += Environment.NewLine + Format(exception.InnerException);
        }

        return details;
    }
}
