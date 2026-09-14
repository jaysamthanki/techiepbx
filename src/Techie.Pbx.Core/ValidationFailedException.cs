namespace Techie.Pbx.Core
{
    public class ValidationFailedException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ValidationFailedException(IReadOnlyList<string> errors)
            : base(string.Join(" ", errors))
        {
            Errors = errors;
        }

        public ValidationFailedException(string error)
            : this(new[] { error })
        {
        }
    }
}
