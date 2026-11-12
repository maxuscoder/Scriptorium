using Microsoft.Win32;

namespace Scriptorium.App.Services;

public interface IPrivateLibraryDialogs
{
    IReadOnlyList<string> PickFiles();
    string? PickFolder();
    string? PickExportPath(string fileName);
}

public sealed class PrivateLibraryDialogs : IPrivateLibraryDialogs
{
    public IReadOnlyList<string> PickFiles()
    {
        var dialog = new OpenFileDialog { Title = "Import into Private Library", Multiselect = true,
            Filter = "Media files|*.avi;*.mkv;*.mov;*.mp4;*.webm;*.wmv|All files|*.*" };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Import a private folder" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    public string? PickExportPath(string fileName)
    {
        var dialog = new SaveFileDialog { Title = "Return decrypted file to regular library", FileName = fileName,
            Filter = "Media file|*.*", OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
