namespace Gannet.Core.Services;

/// <summary>One reader that did not give text. <see cref="Message"/> is gannet's own wording, never text from a server or a page.</summary>
public sealed record ReadFailure(string Reader, string Error, string Message);

public sealed record ReadOutcome(PageContent? Page, IReadOnlyList<ReadFailure> Failures);

/// <summary>Tries each reader in order until one returns text. Failures are reported, never thrown, except cancellation.</summary>
public sealed class ReaderChain(IReadOnlyList<IPageReader> readers)
{
    public IReadOnlyList<IPageReader> Readers { get; } = readers;

    public async Task<ReadOutcome> ReadAsync(Uri url, int maxCharacters, CancellationToken ct)
    {
        var failures = new List<ReadFailure>();
        foreach (var reader in Readers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var page = await reader.ReadAsync(url, maxCharacters, ct);
                if (string.IsNullOrWhiteSpace(page.Markdown))
                {
                    failures.Add(new ReadFailure(reader.Name, "failed", "The page had no readable text."));
                    continue;
                }

                return new ReadOutcome(page, failures);
            }
            catch (ServiceException e)
            {
                // Typed errors carry gannet's own words (they never include a response body).
                failures.Add(new ReadFailure(reader.Name, e.ErrorKind, e.Message));
            }
            catch (HttpRequestException e)
            {
                failures.Add(new ReadFailure(reader.Name, "failed", $"Could not connect ({e.HttpRequestError})."));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failures.Add(new ReadFailure(reader.Name, "failed", "Timed out."));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A bug in a reader must not end the run. Only the type name is kept: the message can carry page or server text.
                failures.Add(new ReadFailure(reader.Name, "unexpected", e.GetType().Name));
            }
        }

        return new ReadOutcome(null, failures);
    }
}
