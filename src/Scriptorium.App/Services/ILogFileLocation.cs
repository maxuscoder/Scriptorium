namespace Scriptorium.App.Services;

public interface ILogFileLocation
{
    string DirectoryPath { get; }

    string FilePathTemplate { get; }
}
