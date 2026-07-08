namespace reromanlee.ConsoleContainer
{
    /// <summary>
    /// Defines a contract for creating text messages, warning
    /// messages or error messages inside a container instance.
    /// </summary>
    public interface IConsoleInstance
    {
        public void CreateText(object source, params string[] messageContent);
        public void CreateText(string source, params string[] messageContent);

        public void CreateWarning(object source, params string[] messageContent);
        public void CreateWarning(string source, params string[] messageContent);

        public void CreateError(object source, params string[] messageContent);
        public void CreateError(string source, params string[] messageContent);
    }
}