namespace PhotoPlatform.Application.Authentication;

public interface IStudioBootstrap
{
    Task<StudioBootstrapResult> RunAsync(StudioBootstrapRequest request, CancellationToken cancellationToken);
}
