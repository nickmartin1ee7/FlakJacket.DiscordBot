namespace FlakJacket.ClassLibrary
{
    public static class StringExtensions
    {
        public static int ToUniformHashCode(this string input) => input.GetHashCode(StringComparison.InvariantCultureIgnoreCase);
    }
}
