namespace PhotoPlatform.Application.Photos;

public interface IPhotoCommitClassifier
{
    PhotoCommitClass Classify(Exception exception);
}
