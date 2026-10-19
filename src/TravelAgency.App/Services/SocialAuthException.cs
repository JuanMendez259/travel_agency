public sealed class SocialAuthException : Exception
{
    public bool IsCancelled { get; }

    public SocialAuthException(string message, bool isCancelled = false)
        : base(message)
    {
        IsCancelled = isCancelled;
    }
}