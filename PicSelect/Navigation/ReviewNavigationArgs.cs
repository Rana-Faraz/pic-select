namespace PicSelect.Navigation;

public sealed record ReviewNavigationArgs(long ProjectId, int IterationNumber, long? PreferredPhotoId = null);
